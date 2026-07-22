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
/// expense shares and the M7 per-event balance to the VietQR payload builder and the QR image renderer.
/// Both QR operations encode one transfer per still-owing member composited into a single labelled PNG:
/// the expense QR bills each unsettled share owed by a non-payer member (amount = that share); the event
/// QR (closed-only) bills each member with a negative balance (amount = |negative balance|). All inputs
/// are resolved through resource-owned application services, so ownership misses surface as their existing
/// 404s (<c>ExpenseNotFound</c> 6000 / <c>EventNotFound</c> 9000); wallet/QR-specific states use the 12xxx
/// codes (incl. <c>NoOutstandingDebtForQr</c> 12003 when nobody owes / everyone is settled). Nothing is
/// persisted (OQ17). M10 gate (OQ5b): both QR operations are Premium-only - a Free caller gets 403
/// PremiumFeatureRequired (13003) before anything is resolved.
/// </summary>
public interface IWalletQrService
{
    Task<QrImageResult> GenerateExpenseQrAsync(string userUuid, string expenseUuid, string? bankAccountUuid, CancellationToken cancellationToken = default);

    Task<QrImageResult> GenerateEventQrAsync(string userUuid, string eventUuid, string? bankAccountUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo một mã QR VietQR riêng cho mỗi thành viên còn nợ trên phiếu chi tiêu (phần gánh chưa đánh dấu
    /// đã trả, khác 0đ, không phải người trả). Mỗi mã được dựng server-side qua <c>RenderSingle</c> và trả
    /// về dưới dạng data URL. Dùng chung tập "ai còn nợ" với <see cref="GenerateExpenseQrAsync"/>. Không lưu gì.
    /// </summary>
    Task<IReadOnlyList<MemberQrResponse>> GenerateExpenseMemberQrsAsync(string userUuid, string expenseUuid, string? bankAccountUuid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tạo một mã QR VietQR riêng cho mỗi thành viên còn nợ trong một đợt đã chốt (cân bằng âm chưa đánh dấu
    /// đã trả). Chỉ áp dụng cho đợt đã chốt (đợt đang mở -&gt; 12002). Mỗi mã được dựng server-side qua
    /// <c>RenderSingle</c> và trả về dưới dạng data URL. Dùng chung tập "ai còn nợ" với
    /// <see cref="GenerateEventQrAsync"/>. Không lưu gì.
    /// </summary>
    Task<IReadOnlyList<MemberQrResponse>> GenerateEventMemberQrsAsync(string userUuid, string eventUuid, string? bankAccountUuid, CancellationToken cancellationToken = default);
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
    private const string PngContentType = "image/png";

    // DI supplies the localizer; unit-test construction (new WalletQrService(...)) falls back to the shared
    // localizer, which resolves the same resx family and honours the request CurrentUICulture (mirrors TierService).
    private readonly IStringLocalizer<StringResources> _localizer = localizer ?? SharedStringLocalizer.Instance;

    public async Task<QrImageResult> GenerateExpenseQrAsync(string userUuid, string expenseUuid, string? bankAccountUuid, CancellationToken cancellationToken = default)
    {
        tierService.EnsurePremiumFeature(MessageKeys.Feature.Qr);

        var account = await ResolveDestinationAsync(userUuid, bankAccountUuid, cancellationToken);

        // Resource-owned expense (miss -> ExpenseNotFound 6000); its shares carry who owes what (M5).
        var expense = await expensesService.GetAsync(userUuid, expenseUuid, cancellationToken);

        // Bill one QR per member who still owes on this expense (shared billing selection - see
        // CollectExpenseBillables). Empty -> NoOutstandingDebtForQr (12003).
        var (contextName, billed) = CollectExpenseBillables(expense);
        if (billed.Count == 0)
            throw new ErrorException(ErrorCodes.NoOutstandingDebtForQr, MessageKeys.Error.NoOutstandingDebtForQr);

        var provider = qrContentResolver.Resolve();
        var items = new List<QrCompositeItem>(billed.Count);
        foreach (var member in billed)
        {
            var payload = await provider.BuildContentAsync(
                new QrContentRequest(account.BankBin, account.AccountNumber, account.AccountHolderName, member.Amount, member.Description),
                cancellationToken);
            var label = $"{member.MemberName}: {FormatMoney(member.Amount)}";
            items.Add(new QrCompositeItem(label, payload));
        }

        // Expense header carries no amount (per-member amounts stay under each member's QR).
        var header = await BuildHeaderAsync(account, contextName, amount: null, cancellationToken);
        var image = qrImageService.RenderComposite(items, header);
        return new QrImageResult(image, PngContentType, $"expense-qr-{expense.Uuid}.png");
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

        // Bill only members who still owe (shared billing selection - see CollectEventBillables). Empty
        // -> NoOutstandingDebtForQr (12003).
        var (contextName, billed) = CollectEventBillables(balance);
        if (billed.Count == 0)
            throw new ErrorException(ErrorCodes.NoOutstandingDebtForQr, MessageKeys.Error.NoOutstandingDebtForQr);

        var provider = qrContentResolver.Resolve();
        var items = new List<QrCompositeItem>(billed.Count);
        foreach (var member in billed)
        {
            var payload = await provider.BuildContentAsync(
                new QrContentRequest(account.BankBin, account.AccountNumber, account.AccountHolderName, member.Amount, member.Description),
                cancellationToken);
            var label = $"{member.MemberName}: {FormatMoney(member.Amount)}";
            items.Add(new QrCompositeItem(label, payload));
        }

        // Event header carries no amount (per-member amounts stay under each member's QR).
        var header = await BuildHeaderAsync(account, contextName, amount: null, cancellationToken);
        var image = qrImageService.RenderComposite(items, header);
        return new QrImageResult(image, PngContentType, $"event-qr-{balance.EventUuid}.png");
    }

    public async Task<IReadOnlyList<MemberQrResponse>> GenerateExpenseMemberQrsAsync(string userUuid, string expenseUuid, string? bankAccountUuid, CancellationToken cancellationToken = default)
    {
        tierService.EnsurePremiumFeature(MessageKeys.Feature.Qr);

        var account = await ResolveDestinationAsync(userUuid, bankAccountUuid, cancellationToken);

        // Resource-owned expense (miss -> ExpenseNotFound 6000); its shares carry who owes what (M5).
        var expense = await expensesService.GetAsync(userUuid, expenseUuid, cancellationToken);

        var (contextName, billed) = CollectExpenseBillables(expense);
        if (billed.Count == 0)
            throw new ErrorException(ErrorCodes.NoOutstandingDebtForQr, MessageKeys.Error.NoOutstandingDebtForQr);

        return await BuildMemberQrsAsync(account, contextName, billed, cancellationToken);
    }

    public async Task<IReadOnlyList<MemberQrResponse>> GenerateEventMemberQrsAsync(string userUuid, string eventUuid, string? bankAccountUuid, CancellationToken cancellationToken = default)
    {
        tierService.EnsurePremiumFeature(MessageKeys.Feature.Qr);

        var account = await ResolveDestinationAsync(userUuid, bankAccountUuid, cancellationToken);

        // Reuse the M7 balance (miss -> EventNotFound 9000); never recompute debt.
        var balance = await statsService.GetEventBalanceAsync(userUuid, eventUuid, cancellationToken);

        // Event QR is closed-only (§4.4/§5): the data must be frozen before it is shared.
        if (!balance.IsClosed)
            throw new ErrorException(ErrorCodes.EventNotClosedForQr, MessageKeys.Error.EventNotClosedForQr);

        var (contextName, billed) = CollectEventBillables(balance);
        if (billed.Count == 0)
            throw new ErrorException(ErrorCodes.NoOutstandingDebtForQr, MessageKeys.Error.NoOutstandingDebtForQr);

        return await BuildMemberQrsAsync(account, contextName, billed, cancellationToken);
    }

    /// <summary>
    /// A still-owing member and the transfer to bill them: the display name, the outstanding amount, and the
    /// VietQR description. Shared by the composite and per-member QR paths so their billed sets cannot diverge.
    /// </summary>
    private sealed record BilledMember(string MemberUuid, string MemberName, decimal Amount, string Description);

    /// <summary>
    /// Selects who still owes on an expense: an unsettled, non-zero share owed by someone other than the payer
    /// (the payer paid the total and never transfers to themselves; the 0đ owner-representative share drops out
    /// via Amount &gt; 0). Order preserved from <c>expense.Shares</c>. ContextName = the expense name.
    /// </summary>
    private static (string ContextName, IReadOnlyList<BilledMember> Billed) CollectExpenseBillables(Models.Expenses.ExpenseResponse expense)
    {
        var billed = expense.Shares
            .Where(share => !share.IsSettled && share.Amount > 0m && share.Member.Uuid != expense.Payer.Uuid)
            .Select(share => new BilledMember(share.Member.Uuid, share.Member.Name, share.Amount, $"{expense.Name} - {share.Member.Name}"))
            .ToList();
        return (expense.Name, billed);
    }

    /// <summary>
    /// Selects who still owes in a closed event: each balance row with <c>Outstanding &gt; 0</c> (the derived
    /// settled-per-member overlay). Order preserved from <c>balance.Rows</c>. ContextName = the event name.
    /// </summary>
    private static (string ContextName, IReadOnlyList<BilledMember> Billed) CollectEventBillables(Models.Stats.EventBalanceResponse balance)
    {
        var billed = balance.Rows
            .Where(row => row.Outstanding > 0m)
            .Select(row => new BilledMember(row.MemberUuid, row.MemberName, row.Outstanding, $"{balance.EventName} - {row.MemberName}"))
            .ToList();
        return (balance.EventName, billed);
    }

    /// <summary>
    /// Renders one single-QR PNG per billed member (order preserved) and returns each as a
    /// <c>data:image/png;base64,&lt;...&gt;</c> data URL. The member name and amount are carried in the header
    /// title / amount row (RenderSingle draws no label under the QR). Pure read - nothing is persisted.
    /// </summary>
    private async Task<IReadOnlyList<MemberQrResponse>> BuildMemberQrsAsync(BankAccount account, string contextName, IReadOnlyList<BilledMember> billed, CancellationToken cancellationToken)
    {
        var provider = qrContentResolver.Resolve();
        var results = new List<MemberQrResponse>(billed.Count);
        foreach (var member in billed)
        {
            var payload = await provider.BuildContentAsync(
                new QrContentRequest(account.BankBin, account.AccountNumber, account.AccountHolderName, member.Amount, member.Description),
                cancellationToken);
            var header = await BuildHeaderAsync(account, $"{contextName} - {member.MemberName}", member.Amount, cancellationToken);
            var png = qrImageService.RenderSingle(payload, header);
            var image = "data:image/png;base64," + Convert.ToBase64String(png);
            results.Add(new MemberQrResponse
            {
                MemberUuid = member.MemberUuid,
                MemberName = member.MemberName,
                Amount = member.Amount,
                Image = image,
            });
        }

        return results;
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
