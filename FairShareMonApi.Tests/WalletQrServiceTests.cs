using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Stats;
using FairShareMonApi.Services.Api.Wallet;
using FairShareMonApi.Tests.Infrastructure;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>WalletQrService</c> over fakes for the wallet repo / expense service / stats
/// service plus the REAL <see cref="VietQrPayloadBuilder"/> and a capturing fake image service (no DB).
/// Proves the orchestration: destination resolution (default vs owned override; miss -> 12000; none ->
/// 12001); expense QR amount = <c>expense.Total</c>; <c>format=payload</c> returns the raw VietQR string;
/// event QR closed-only (open -> 12002), nobody-owes -> 12003, else exactly one composite entry per
/// negative-balance member with amount = |Balance| and a label carrying the member name.
/// </summary>
public class WalletQrServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-00000000c001";
    private const string ExpenseUuid = "0198a5c2-0000-7000-8000-0000000e0001";
    private const string EventUuid = "0198a5c2-0000-7000-8000-0000000e0002";

    private readonly FakeBankAccountRepository _accounts = new();
    private readonly FakeExpensesService _expenses = new();
    private readonly FakeStatsService _stats = new();
    private readonly CapturingQrImageService _images = new();
    // Pass-through tier double for the orchestration tests; the Premium QR gate is proved separately
    // (below + at the endpoint level).
    private readonly FakeTierService _tier = new();

    private WalletQrService CreateService() =>
        new(_accounts, _expenses, _stats, _tier, new VietQrPayloadBuilder(), _images);

    private BankAccount AddDefaultAccount(string bin = "970436", string number = "0123456789")
    {
        var account = new BankAccount
        {
            BankBin = bin, BankName = "Vietcombank", AccountNumber = number,
            AccountHolderName = "Nguyen Van A", IsDefault = true
        };
        _accounts.Accounts.Add(account);
        return account;
    }

    // ---- Destination resolution -------------------------------------------------------------------

    [Fact]
    public async Task GenerateExpenseQr_NoBankAccount_Throws12001()
    {
        _expenses.Expense = new ExpenseResponse { Uuid = ExpenseUuid, Name = "Ăn tối", Total = 500_000m };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null, null));

        Assert.Equal(ErrorCodes.NoBankAccountForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateExpenseQr_OverrideAccountMiss_Throws12000()
    {
        AddDefaultAccount();
        _expenses.Expense = new ExpenseResponse { Uuid = ExpenseUuid, Name = "Ăn tối", Total = 500_000m };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, "no-such-account", null));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    [Fact]
    public async Task GenerateExpenseQr_OverrideAccountOwned_UsesTheOverrideDestination()
    {
        AddDefaultAccount(bin: "970436", number: "0123456789");
        var other = new BankAccount
        {
            BankBin = "970422", BankName = "MB Bank", AccountNumber = "9998887776",
            AccountHolderName = "Tran Thi B", IsDefault = false
        };
        _accounts.Accounts.Add(other);
        _expenses.Expense = new ExpenseResponse { Uuid = ExpenseUuid, Name = "Ăn tối", Total = 500_000m };

        var result = await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, other.Uuid, "payload");

        var beneficiary = ParseTlv(ParseTlv(ParseTlv(result.Payload!)["38"])["01"]);
        Assert.Equal("970422", beneficiary["00"]); // override BIN, not the default's
        Assert.Equal("9998887776", beneficiary["01"]);
    }

    // ---- Expense QR ------------------------------------------------------------------------------

    [Fact]
    public async Task GenerateExpenseQr_Default_AmountEqualsExpenseTotal()
    {
        AddDefaultAccount();
        _expenses.Expense = new ExpenseResponse { Uuid = ExpenseUuid, Name = "Ăn tối", Total = 750_000m };

        var result = await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null, null);

        Assert.False(result.IsPayload);
        Assert.NotNull(result.Image);
        Assert.Equal("image/png", result.Image!.ContentType);
        Assert.Contains(ExpenseUuid, result.Image.FileName);

        // The single rendered payload carries amount = the expense total.
        var payload = Assert.Single(_images.SinglePayloads);
        Assert.Equal("750000", ParseTlv(payload)["54"]);
    }

    [Fact]
    public async Task GenerateExpenseQr_FormatPayload_ReturnsRawVietQrStringWithoutRendering()
    {
        AddDefaultAccount();
        _expenses.Expense = new ExpenseResponse { Uuid = ExpenseUuid, Name = "Ăn tối", Total = 500_000m };

        var result = await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null, "payload");

        Assert.True(result.IsPayload);
        Assert.Null(result.Image);
        Assert.StartsWith("0002010102", result.Payload); // EMVCo format + dynamic point-of-initiation
        Assert.Empty(_images.SinglePayloads);            // no image was rendered
        Assert.Equal("500000", ParseTlv(result.Payload!)["54"]);
    }

    [Fact]
    public async Task GenerateExpenseQr_ExpenseMissBubblesUpAs6000()
    {
        AddDefaultAccount();
        _expenses.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null, null));

        Assert.Equal(ErrorCodes.ExpenseNotFound, exception.Code);
    }

    // ---- Event QR --------------------------------------------------------------------------------

    [Fact]
    public async Task GenerateEventQr_OpenEvent_Throws12002()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = false,
            Rows = [Row("Cường", -500_000m)]
        };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.EventNotClosedForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateEventQr_NobodyOwes_Throws12003()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows = [Row("Bình", 300_000m), Row("An", 0m)]
        };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.NoOutstandingDebtForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateEventQr_ClosedWithDebtors_ComposesOnePerNegativeBalanceMember()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows =
            [
                Row("Bình", 300_000m),   // positive - excluded
                Row("An", 0m),           // zero - excluded
                Row("Cường", -500_000m), // owing - included
                Row("Dũng", -125_000m)   // owing - included
            ]
        };

        var result = await CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null);

        Assert.Equal("image/png", result.ContentType);
        Assert.Contains(EventUuid, result.FileName);

        var items = Assert.Single(_images.CompositeBatches); // one composite call
        Assert.Equal(2, items.Count);                        // exactly the two debtors

        // Amount per debtor = |Balance|; label carries the member name.
        var cuong = items.Single(item => item.Label.Contains("Cường"));
        Assert.Equal("500000", ParseTlv(cuong.Payload)["54"]);

        var dung = items.Single(item => item.Label.Contains("Dũng"));
        Assert.Equal("125000", ParseTlv(dung.Payload)["54"]);
    }

    [Fact]
    public async Task GenerateEventQr_NoBankAccount_Throws12001BeforeBalanceLookup()
    {
        _stats.Balance = new EventBalanceResponse { EventUuid = EventUuid, IsClosed = true, Rows = [Row("Cường", -1m)] };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.NoBankAccountForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateEventQr_EventMissBubblesUpAs9000()
    {
        AddDefaultAccount();
        _stats.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    // ---- M10 Premium feature-gate (both QR ops gated, fires before anything is resolved) ----------

    [Fact]
    public async Task GenerateExpenseQr_FreeCaller_Throws13003BeforeResolvingDestination()
    {
        // No bank account added: if the gate did NOT fire first, destination resolution would throw
        // 12001 instead - asserting 13003 proves the gate runs before anything is resolved.
        _tier.PremiumFeatureCode = ErrorCodes.PremiumFeatureRequired;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null, null));

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
    }

    [Fact]
    public async Task GenerateEventQr_FreeCaller_Throws13003BeforeResolvingDestination()
    {
        _tier.PremiumFeatureCode = ErrorCodes.PremiumFeatureRequired;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
    }

    private static MemberBalanceRow Row(string name, decimal balance) =>
        new() { MemberUuid = Guid.NewGuid().ToString(), MemberName = name, Balance = balance };

    private static Dictionary<string, string> ParseTlv(string data)
    {
        var map = new Dictionary<string, string>();
        var i = 0;
        while (i < data.Length)
        {
            var id = data.Substring(i, 2);
            var length = int.Parse(data.Substring(i + 2, 2));
            map[id] = data.Substring(i + 4, length);
            i += 4 + length;
        }

        return map;
    }

    // ---- Fakes -----------------------------------------------------------------------------------

    private sealed class FakeBankAccountRepository : IBankAccountRepository
    {
        public List<BankAccount> Accounts { get; } = [];

        public Task<BankAccount?> GetByUuidAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts.FirstOrDefault(account => account.Uuid == bankAccountUuid));

        public Task<BankAccount?> GetDefaultAsync(string userUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts.FirstOrDefault(account => account.IsDefault));

        public Task<IReadOnlyList<BankAccount>> ListByUserAsync(string userUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BankAccount?> CreateAsync(string userUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(string userUuid, string bankAccountUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetDefaultAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<BankAccount> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();
        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeExpensesService : IExpensesService
    {
        public ExpenseResponse? Expense { get; set; }
        public bool ThrowNotFound { get; set; }

        public Task<ExpenseResponse> GetAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default)
        {
            if (ThrowNotFound)
                throw new ErrorException(ErrorCodes.ExpenseNotFound, "Không tìm thấy phiếu chi tiêu.");
            return Task.FromResult(Expense!);
        }

        public Task<IReadOnlyList<ExpenseSummaryResponse>> ListAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> CreateAsync(string userUuid, CreateExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> UpdateAsync(string userUuid, string expenseUuid, UpdateExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetSettledAsync(string userUuid, string expenseUuid, SetSettledRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> AssignEventAsync(string userUuid, string expenseUuid, AssignEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditLogResponse>> GetHistoryAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeStatsService : IStatsService
    {
        public EventBalanceResponse? Balance { get; set; }
        public bool ThrowNotFound { get; set; }

        public Task<EventBalanceResponse> GetEventBalanceAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            if (ThrowNotFound)
                throw new ErrorException(ErrorCodes.EventNotFound, "Không tìm thấy đợt chi tiêu.");
            return Task.FromResult(Balance!);
        }

        public Task<OverviewStatsResponse> GetOverviewAsync(string userUuid, StatsRangeRequest range, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ByCategoryStatsResponse> GetByCategoryAsync(string userUuid, ByCategoryStatsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingQrImageService : IQrImageService
    {
        public List<string> SinglePayloads { get; } = [];
        public List<IReadOnlyList<QrCompositeItem>> CompositeBatches { get; } = [];

        public byte[] RenderSingle(string payload)
        {
            SinglePayloads.Add(payload);
            return [0x89, 0x50, 0x4E, 0x47];
        }

        public byte[] RenderComposite(IReadOnlyList<QrCompositeItem> items)
        {
            CompositeBatches.Add(items);
            return [0x89, 0x50, 0x4E, 0x47];
        }
    }
}
