using System.Globalization;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Banks;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Stats;
using FairShareMonApi.Services.Api.Tiers;

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
    IQrImageService qrImageService) : IWalletQrService
{
    private const string PayloadFormat = "payload";
    private const string PngContentType = "image/png";

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

        var image = qrImageService.RenderSingle(payload);
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

        // A negative per-event balance means the member still owes; its magnitude is the amount (§3.7).
        var owing = balance.Rows.Where(row => row.Balance < 0m).ToList();
        if (owing.Count == 0)
            throw new ErrorException(ErrorCodes.NoOutstandingDebtForQr, MessageKeys.Error.NoOutstandingDebtForQr);

        var provider = qrContentResolver.Resolve();
        var items = new List<QrCompositeItem>(owing.Count);
        foreach (var row in owing)
        {
            var amount = -row.Balance;
            var payload = await provider.BuildContentAsync(
                new QrContentRequest(account.BankBin, account.AccountNumber, account.AccountHolderName, amount, $"{balance.EventName} - {row.MemberName}"),
                cancellationToken);
            var label = $"{row.MemberName}: {FormatMoney(amount)}";
            items.Add(new QrCompositeItem(label, payload));
        }

        var image = qrImageService.RenderComposite(items);
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

    /// <summary>Formats a VND amount for the composite label, e.g. 500000 -&gt; "500.000đ".</summary>
    private static string FormatMoney(decimal amount)
    {
        var format = new NumberFormatInfo { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NumberDecimalDigits = 0 };
        return amount.ToString("N0", format) + "đ";
    }
}
