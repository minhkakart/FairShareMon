using FairShareMonApi.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Tiers;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the M10 <see cref="TierService"/> over fake count repositories, a fake
/// <see cref="IContextAuthenticated"/> (FREE / PREMIUM / null caller), and an in-memory
/// <see cref="IConfiguration"/> (no DB). Proves each <c>EnsureCanCreate*</c> throws the right 13xxx code
/// AT the limit and passes below it (exact boundary N allowed / N+1 blocked); PREMIUM bypasses all three;
/// <c>EnsurePremiumFeature</c> throws 13003 for FREE and passes for PREMIUM; a null/unknown tier is
/// treated as FREE (fail-safe); the config numbers are honoured (low injected limits); and the
/// expenses-per-month guard computes the current +7 calendar-month <c>[from, to)</c> UTC window.
/// </summary>
public class TierServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-00000000f001";

    private readonly FakeContext _context = new();
    private readonly FakeMemberCounter _members = new();
    private readonly FakeEventCounter _events = new();
    private readonly FakeExpenseCounter _expenses = new();

    private TierService CreateService(int? maxMembers = null, int? maxOpenEvents = null, int? maxExpensesPerMonth = null) =>
        new(_context, _members, _events, _expenses, BuildConfig(maxMembers, maxOpenEvents, maxExpensesPerMonth));

    private static IConfiguration BuildConfig(int? maxMembers, int? maxOpenEvents, int? maxExpensesPerMonth)
    {
        var dict = new Dictionary<string, string?>();
        if (maxMembers is not null) dict["Tiers:Free:MaxMembers"] = maxMembers.Value.ToString();
        if (maxOpenEvents is not null) dict["Tiers:Free:MaxOpenEvents"] = maxOpenEvents.Value.ToString();
        if (maxExpensesPerMonth is not null) dict["Tiers:Free:MaxExpensesPerMonth"] = maxExpensesPerMonth.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private void AsFree() => _context.AuthenticatedUser = new AuthenticatedUser { Id = UserUuid, Username = "an", Tier = UserTiers.Free };
    private void AsPremium() => _context.AuthenticatedUser = new AuthenticatedUser { Id = UserUuid, Username = "an", Tier = UserTiers.Premium };

    // ---- Members (13000) --------------------------------------------------------------------------

    [Fact]
    public async Task EnsureCanCreateMember_FreeBelowLimit_Passes()
    {
        AsFree();
        _members.Count = 2; // below 3

        await CreateService(maxMembers: 3).EnsureCanCreateMemberAsync(UserUuid); // no throw
    }

    [Fact]
    public async Task EnsureCanCreateMember_FreeAtLimit_Throws13000()
    {
        AsFree();
        _members.Count = 3; // exactly at 3 -> the (N+1)-th create is blocked

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService(maxMembers: 3).EnsureCanCreateMemberAsync(UserUuid));

        Assert.Equal(ErrorCodes.MemberLimitReached, exception.Code);
    }

    [Fact]
    public async Task EnsureCanCreateMember_PremiumAtLimit_Passes_AndSkipsTheCount()
    {
        AsPremium();
        _members.Count = 1_000; // way over any limit

        await CreateService(maxMembers: 3).EnsureCanCreateMemberAsync(UserUuid); // no throw

        Assert.False(_members.WasCounted); // Premium bypasses before touching the DB count
    }

    // ---- Open events (13001) ----------------------------------------------------------------------

    [Fact]
    public async Task EnsureCanCreateOpenEvent_FreeBelowLimit_Passes()
    {
        AsFree();
        _events.Count = 1;

        await CreateService(maxOpenEvents: 2).EnsureCanCreateOpenEventAsync(UserUuid);
    }

    [Fact]
    public async Task EnsureCanCreateOpenEvent_FreeAtLimit_Throws13001()
    {
        AsFree();
        _events.Count = 2;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService(maxOpenEvents: 2).EnsureCanCreateOpenEventAsync(UserUuid));

        Assert.Equal(ErrorCodes.OpenEventLimitReached, exception.Code);
    }

    [Fact]
    public async Task EnsureCanCreateOpenEvent_PremiumAtLimit_Passes()
    {
        AsPremium();
        _events.Count = 1_000;

        await CreateService(maxOpenEvents: 2).EnsureCanCreateOpenEventAsync(UserUuid);
        Assert.False(_events.WasCounted);
    }

    // ---- Monthly expenses (13002) -----------------------------------------------------------------

    [Fact]
    public async Task EnsureCanCreateExpense_FreeBelowLimit_Passes()
    {
        AsFree();
        _expenses.Count = 4;

        await CreateService(maxExpensesPerMonth: 5).EnsureCanCreateExpenseAsync(UserUuid);
    }

    [Fact]
    public async Task EnsureCanCreateExpense_FreeAtLimit_Throws13002()
    {
        AsFree();
        _expenses.Count = 5;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService(maxExpensesPerMonth: 5).EnsureCanCreateExpenseAsync(UserUuid));

        Assert.Equal(ErrorCodes.MonthlyExpenseLimitReached, exception.Code);
    }

    [Fact]
    public async Task EnsureCanCreateExpense_PremiumAtLimit_Passes()
    {
        AsPremium();
        _expenses.Count = 1_000;

        await CreateService(maxExpensesPerMonth: 5).EnsureCanCreateExpenseAsync(UserUuid);
        Assert.False(_expenses.WasCounted);
    }

    [Fact]
    public async Task EnsureCanCreateExpense_ComputesCurrentPlus7CalendarMonthUtcWindow()
    {
        AsFree();
        _expenses.Count = 0; // under any limit; we only care about the window it queried

        // Expected window, computed the same way TierService does (fixed +7, no TimeZoneInfo). Captured
        // right before the call; a month rollover between here and the service call is sub-millisecond.
        var offset = TimeSpan.FromHours(7);
        var nowLocal = DateTime.UtcNow.Add(offset);
        var monthStartLocal = new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var expectedFrom = monthStartLocal.Subtract(offset);
        var expectedTo = monthStartLocal.AddMonths(1).Subtract(offset);

        await CreateService(maxExpensesPerMonth: 5).EnsureCanCreateExpenseAsync(UserUuid);

        Assert.Equal(expectedFrom, _expenses.LastFrom);
        Assert.Equal(expectedTo, _expenses.LastTo);
        // Both bounds, converted back to +7 local, are the first day of a month at 00:00 - i.e. the
        // window is exactly one calendar month measured at the local (+7) month boundaries.
        var localStart = _expenses.LastFrom!.Value.Add(offset);
        var localEnd = _expenses.LastTo!.Value.Add(offset);
        Assert.Equal(1, localStart.Day);
        Assert.Equal(0, localStart.Hour);
        Assert.Equal(1, localEnd.Day);
        Assert.Equal(0, localEnd.Hour);
        Assert.Equal(localStart.AddMonths(1), localEnd);
    }

    // ---- Premium feature-gate (13003) -------------------------------------------------------------

    [Fact]
    public void EnsurePremiumFeature_Free_Throws13003()
    {
        AsFree();

        var exception = Assert.Throws<ErrorException>(() => CreateService().EnsurePremiumFeature("ví ngân hàng"));

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
    }

    [Fact]
    public void EnsurePremiumFeature_Premium_Passes()
    {
        AsPremium();

        CreateService().EnsurePremiumFeature("tạo mã QR"); // no throw
    }

    // ---- Fail-safe: null / unknown tier is treated as Free ----------------------------------------

    [Fact]
    public async Task NullPrincipal_TreatedAsFree_MemberLimitEnforced()
    {
        _context.AuthenticatedUser = null; // anonymous / no principal -> fail-safe Free
        _members.Count = 3;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService(maxMembers: 3).EnsureCanCreateMemberAsync(UserUuid));

        Assert.Equal(ErrorCodes.MemberLimitReached, exception.Code);
    }

    [Fact]
    public void UnknownTier_TreatedAsFree_PremiumFeatureGated()
    {
        _context.AuthenticatedUser = new AuthenticatedUser { Id = UserUuid, Username = "an", Tier = "MYSTERY" };

        var exception = Assert.Throws<ErrorException>(() => CreateService().EnsurePremiumFeature("ví ngân hàng"));

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
    }

    // ---- Config-driven numbers (and in-code defaults) ---------------------------------------------

    [Fact]
    public async Task ConfiguredLowLimit_IsHonoured()
    {
        AsFree();
        _members.Count = 1;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService(maxMembers: 1).EnsureCanCreateMemberAsync(UserUuid));

        Assert.Equal(ErrorCodes.MemberLimitReached, exception.Code);
    }

    [Fact]
    public async Task MissingConfig_UsesInCodeDefault25Members()
    {
        AsFree();
        _members.Count = 24; // under the 25 default

        await CreateService().EnsureCanCreateMemberAsync(UserUuid); // no throw at 24

        _members.Count = 25; // at the 25 default -> blocked
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().EnsureCanCreateMemberAsync(UserUuid));
        Assert.Equal(ErrorCodes.MemberLimitReached, exception.Code);
    }

    [Fact]
    public async Task LimitMessage_IncludesTheConfiguredNumber()
    {
        AsFree();
        _members.Count = 7;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService(maxMembers: 7).EnsureCanCreateMemberAsync(UserUuid));

        Assert.Contains("7", exception.Message);        // interpolated limit number
        Assert.Contains("Premium", exception.Message);  // names the upsell
    }

    // ---- Fakes ------------------------------------------------------------------------------------

    private sealed class FakeContext : IContextAuthenticated
    {
        public AuthenticatedUser? AuthenticatedUser { get; set; }
    }

    private sealed class FakeMemberCounter : IMemberRepository
    {
        public int Count { get; set; }
        public bool WasCounted { get; private set; }

        public Task<int> CountActiveByUserAsync(string userUuid, CancellationToken cancellationToken = default)
        {
            WasCounted = true;
            return Task.FromResult(Count);
        }

        public Task<IReadOnlyList<Member>> ListByUserAsync(string userUuid, bool includeDeleted, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Member?> GetByUuidAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Member?> CreateAsync(string userUuid, Member member, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Member?> RenameAsync(string userUuid, string memberUuid, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SoftDeleteAsync(string userUuid, string memberUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasOwnerRepresentativeAsync(string userUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetUserUuidsWithoutOwnerRepresentativeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<Member> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();
        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeEventCounter : IEventRepository
    {
        public int Count { get; set; }
        public bool WasCounted { get; private set; }

        public Task<int> CountOpenByUserAsync(string userUuid, CancellationToken cancellationToken = default)
        {
            WasCounted = true;
            return Task.FromResult(Count);
        }

        public Task<IReadOnlyList<Event>> ListByUserAsync(string userUuid, EventFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Event?> GetByUuidAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventWriteResult<Event>> CreateAsync(string userUuid, CreateEventData data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventWriteResult<Event>> UpdateAsync(string userUuid, string eventUuid, UpdateEventData data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventWriteStatus> CloseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventWriteStatus> DeleteAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<Event> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();
        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeExpenseCounter : IExpenseRepository
    {
        public int Count { get; set; }
        public bool WasCounted { get; private set; }
        public DateTime? LastFrom { get; private set; }
        public DateTime? LastTo { get; private set; }

        public Task<int> CountByUserInRangeAsync(string userUuid, DateTime fromUtcInclusive, DateTime toUtcExclusive, CancellationToken cancellationToken = default)
        {
            WasCounted = true;
            LastFrom = fromUtcInclusive;
            LastTo = toUtcExclusive;
            return Task.FromResult(Count);
        }

        public Task<IReadOnlyList<Expense>> ListByUserAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Expense?> GetByUuidAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteResult<Expense>> CreateAsync(string userUuid, CreateExpenseData data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteResult<Expense>> UpdateGeneralInfoAsync(string userUuid, string expenseUuid, UpdateExpenseData data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteStatus> DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteStatus> SetSettledAsync(string userUuid, string expenseUuid, bool isSettled, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteResult<Expense>> AssignEventAsync(string userUuid, string expenseUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteStatus> RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<Expense> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();
        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
