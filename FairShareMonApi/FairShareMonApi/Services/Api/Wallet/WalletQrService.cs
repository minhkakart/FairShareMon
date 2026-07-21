using System.Globalization;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Banks;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Stats;
using FairShareMonApi.Services.Api.Tiers;
using Microsoft.Extensions.Localization;

namespace FairShareMonApi.Services.Api.Wallet;

/// <summary>
/// On-demand VietQR generation (The-ideal.md §3.5/§3.10/§5) - the seam that ties the wallet, the M5
/// expense total and the M7 per-event balance to the VietQR payload builder and the QR image renderer.
/// The expense QR encodes one transfer for the whole expense (amount = derived total); the event QR
/// (closed-only) encodes one transfer per still-owing member (amount = |negative balance|) composited
/// into a single labelled PNG. All inputs are resolved through resource-owned application services, so
/// ownership misses surface as their existing 404s (<c>ExpenseNotFound</c> 6000 / <c>EventNotFound</c>
/// 9000); wallet/QR-specific states use the 12xxx codes. Nothing is persisted (OQ17). No tier gate at
/// M9 - this service is the single seam a later tier mechanism can gate (OQ14). M10 activates that
/// seam (OQ5b): both QR operations are Premium-only - a Free caller gets 403 PremiumFeatureRequired
/// (13003) before anything is resolved.
/// </summary>
public interface IWalletQrService
{
    Task<ExpenseQrResult> GenerateExpenseQrAsync(string userUuid, string expenseUuid, string? bankAccountUuid, string? format, CancellationToken cancellationToken = default);

    Task<QrImageResult> GenerateEventQrAsync(string userUuid, string eventUuid, string? bankAccountUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IWalletQrService))]
public sealed class WalletQrService(
    IBankAccountRepository bankAccountRepository,
    IExpensesService expensesService,
    IStatsService statsService,
    ITierService tierService,
    IQrContentProviderResolver qrContentResolver,
    IQrImageService qrImageService,
    IBankDirectoryService bankDirectory,
    IStringLocalizer<StringResources>? localizer = null) : IWalletQrService
{
    private const string PayloadFormat = "payload";
    private const string PngContentType = "image/png";

    // DI supplies the localizer; unit-test construction (new WalletQrService(...)) falls back to the shared
    // localizer, which resolves the same resx family and honours the request CurrentUICulture (mirrors TierService).
    private readonly IStringLocalizer<StringResources> _localizer = localizer ?? SharedStringLocalizer.Instance;

    public async Task<ExpenseQrResult> GenerateExpenseQrAsync(string userUuid, string expenseUuid, string? bankAccountUuid, string? format, CancellationToken cancellationToken = default)
    {
        tierService.EnsurePremiumFeature(MessageKeys.Feature.Qr);

        var account = await ResolveDestinationAsync(userUuid, bankAccountUuid, cancellationToken);

        // Resource-owned expense (miss -> ExpenseNotFound 6000); Total is the derived SUM(shares) (M5).
        var expense = await expensesService.GetAsync(userUuid, expenseUuid, cancellationToken);

        var provider = qrContentResolver.Resolve();
        var payload = await provider.BuildContentAsync(
            new QrContentRequest(account.BankBin, account.AccountNumber, account.AccountHolderName, expense.Total, expense.Name),
            cancellationToken);

        if (IsPayloadFormat(format))
            return ExpenseQrResult.FromPayload(payload);

        // Build the header only on the image path so the payload short-circuit does no directory lookup.
        var header = await BuildHeaderAsync(account, expense.Name, expense.Total, cancellationToken);
        var image = qrImageService.RenderSingle(payload, header);
        return ExpenseQrResult.FromImage(new QrImageResult(image, PngContentType, $"expense-qr-{expense.Uuid}.png"));
    }

    public async Task<QrImageResult> GenerateEventQrAsync(string userUuid, string eventUuid, string? bankAccountUuid, CancellationToken cancellationToken = default)
    {
        tierService.EnsurePremiumFeature(MessageKeys.Feature.Qr);

        var account = await ResolveDestinationAsync(userUuid, bankAccountUuid, cancellationToken);

        // Reuse the M7 balance (miss -> EventNotFound 9000); never recompute debt.
        var balance = await statsService.GetEventBalanceAsync(userUuid, eventUuid, cancellationToken);

        // Event QR is closed-only (§4.4/§5): the data must be frozen before it is shared.
        if (!balance.IsClosed)
            throw new ErrorException(ErrorCodes.EventNotClosedForQr, MessageKeys.Error.EventNotClosedForQr);

        // Bill only members who still owe AND have not been marked settled: the derived "outstanding"
        // overlay (settled-per-member OQ13a). outstanding = -balance for an uncleared owing member, 0
        // otherwise - so regenerating after some members pay bills only the remainder.
        var owing = balance.Rows.Where(row => row.Outstanding > 0m).ToList();
        if (owing.Count == 0)
            throw new ErrorException(ErrorCodes.NoOutstandingDebtForQr, MessageKeys.Error.NoOutstandingDebtForQr);

        var provider = qrContentResolver.Resolve();
        var items = new List<QrCompositeItem>(owing.Count);
        foreach (var row in owing)
        {
            var amount = row.Outstanding;
            var payload = await provider.BuildContentAsync(
                new QrContentRequest(account.BankBin, account.AccountNumber, account.AccountHolderName, amount, $"{balance.EventName} - {row.MemberName}"),
                cancellationToken);
            var label = $"{row.MemberName}: {FormatMoney(amount)}";
            items.Add(new QrCompositeItem(label, payload));
        }

        // Event header carries no amount (per-member amounts stay under each member's QR).
        var header = await BuildHeaderAsync(account, balance.EventName, amount: null, cancellationToken);
        var image = qrImageService.RenderComposite(items, header);
        return new QrImageResult(image, PngContentType, $"event-qr-{balance.EventUuid}.png");
    }

    /// <summary>
    /// Resolves the QR destination: the <paramref name="bankAccountUuid"/> override (resource-owned,
    /// miss -&gt; 12000) if supplied, else the user's default account; none -&gt; 12001 (OQ8/OQ11).
    /// </summary>
    private async Task<BankAccount> ResolveDestinationAsync(string userUuid, string? bankAccountUuid, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(bankAccountUuid))
        {
            return await bankAccountRepository.GetByUuidAsync(userUuid, bankAccountUuid, cancellationToken)
                ?? throw new ErrorException(ErrorCodes.BankAccountNotFound, MessageKeys.Error.BankAccountNotFound);
        }

        return await bankAccountRepository.GetDefaultAsync(userUuid, cancellationToken)
            ?? throw new ErrorException(ErrorCodes.NoBankAccountForQr, MessageKeys.Error.NoBankAccountForQr);
    }

    private static bool IsPayloadFormat(string? format) =>
        !string.IsNullOrWhiteSpace(format) && format.Trim().Equals(PayloadFormat, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the QR-image header from the destination account: the branded bank name (VietQR directory
    /// ShortName -&gt; Name -&gt; the account's saved BankName on a miss/blank), the localized field labels
    /// (request culture), and the amount (only when <paramref name="amount"/> is non-null - the event header
    /// passes <c>null</c>). Labels are resolved on the request thread so they match the response culture.
    /// </summary>
    private async Task<QrHeader> BuildHeaderAsync(BankAccount account, string title, decimal? amount, CancellationToken cancellationToken)
    {
        var bankName = await ResolveBankNameAsync(account, cancellationToken);

        string? amountLabel = null;
        string? amountText = null;
        if (amount.HasValue)
        {
            amountLabel = _localizer[MessageKeys.Qr.Header.Amount].Value;
            amountText = FormatMoney(amount.Value);
        }

        return new QrHeader(
            Title: title,
            BankLabel: _localizer[MessageKeys.Qr.Header.Bank].Value,
            BankName: bankName,
            HolderLabel: _localizer[MessageKeys.Qr.Header.AccountHolder].Value,
            AccountHolderName: account.AccountHolderName,
            NumberLabel: _localizer[MessageKeys.Qr.Header.AccountNumber].Value,
            AccountNumber: account.AccountNumber,
            AmountLabel: amountLabel,
            AmountText: amountText);
    }

    /// <summary>Resolves the branded bank name by BIN from the shared directory: ShortName -&gt; Name -&gt; the account's saved BankName.</summary>
    private async Task<string> ResolveBankNameAsync(BankAccount account, CancellationToken cancellationToken)
    {
        var banks = await bankDirectory.ListAsync(cancellationToken);
        var bank = banks.FirstOrDefault(b => b.Bin == account.BankBin);
        if (bank is not null)
        {
            if (!string.IsNullOrWhiteSpace(bank.ShortName))
                return bank.ShortName;
            if (!string.IsNullOrWhiteSpace(bank.Name))
                return bank.Name;
        }

        return account.BankName;
    }

    /// <summary>Formats a VND amount for the composite label, e.g. 500000 -&gt; "500.000đ".</summary>
    private static string FormatMoney(decimal amount)
    {
        var format = new NumberFormatInfo { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NumberDecimalDigits = 0 };
        return amount.ToString("N0", format) + "đ";
    }
}
