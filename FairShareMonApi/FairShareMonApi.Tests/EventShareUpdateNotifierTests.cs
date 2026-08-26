using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Share;
using FairShareMonApi.Tests.Infrastructure;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="EventShareUpdateNotifier"/> over fakes for
/// <see cref="IEventShareLinkRepository"/> / <see cref="IExpenseRepository"/> /
/// <see cref="IEventShareStreamBroadcaster"/> (no DB, no Redis, no HTTP). Proves:
/// <c>NotifyEventChangedAsync</c> only publishes when an active link exists and swallows (logs, never
/// rethrows) a repository failure (Decision 3); <c>NotifyExpenseChangedAsync</c> short-circuits before
/// even resolving the active link for a loose expense, and otherwise delegates correctly.
/// </summary>
public class EventShareUpdateNotifierTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-00000000a001";
    private const string EventUuid = "0198a5c2-0000-7000-8000-0000000e0a02";
    private const string ExpenseUuid = "0198a5c2-0000-7000-8000-0000000ea003";
    private const string ActiveToken = "active-token";

    private readonly FakeEventShareLinkRepository _shareLinkRepository = new();
    private readonly FakeExpenseRepository _expenseRepository = new();
    private readonly FakeStreamBroadcaster _broadcaster = new();
    private readonly CapturingLogger<EventShareUpdateNotifier> _logger = new();

    private EventShareUpdateNotifier CreateNotifier() =>
        new(_shareLinkRepository, _expenseRepository, _broadcaster, _logger);

    // ---------------------------- NotifyEventChangedAsync ----------------------------

    [Fact]
    public async Task NotifyEventChangedAsync_ActiveLinkExists_PublishesUpdatedWithItsToken()
    {
        _shareLinkRepository.Active = new EventShareLink { Token = ActiveToken };

        await CreateNotifier().NotifyEventChangedAsync(UserUuid, EventUuid);

        Assert.Equal(1, _broadcaster.PublishUpdatedCalls);
        Assert.Equal(ActiveToken, _broadcaster.LastUpdatedToken);
        Assert.Equal(UserUuid, _shareLinkRepository.LastUserUuid);
        Assert.Equal(EventUuid, _shareLinkRepository.LastEventUuid);
    }

    [Fact]
    public async Task NotifyEventChangedAsync_NoActiveLink_NeverPublishes()
    {
        _shareLinkRepository.Active = null;

        await CreateNotifier().NotifyEventChangedAsync(UserUuid, EventUuid);

        Assert.Equal(0, _broadcaster.PublishUpdatedCalls);
    }

    [Fact]
    public async Task NotifyEventChangedAsync_RepositoryThrows_SwallowsLogsAndNeverRethrows()
    {
        _shareLinkRepository.ThrowOnGetActive = true;

        await CreateNotifier().NotifyEventChangedAsync(UserUuid, EventUuid); // must not throw

        Assert.Equal(0, _broadcaster.PublishUpdatedCalls);
        Assert.True(_logger.HasWarning); // logged, not silently swallowed
    }

    // ---------------------------- NotifyExpenseChangedAsync ----------------------------

    [Fact]
    public async Task NotifyExpenseChangedAsync_ExpenseInEventWithActiveLink_ResolvesThenPublishes()
    {
        _expenseRepository.EventUuidResult = EventUuid;
        _shareLinkRepository.Active = new EventShareLink { Token = ActiveToken };

        await CreateNotifier().NotifyExpenseChangedAsync(UserUuid, ExpenseUuid);

        Assert.Equal(1, _broadcaster.PublishUpdatedCalls);
        Assert.Equal(ActiveToken, _broadcaster.LastUpdatedToken);
        Assert.Equal(EventUuid, _shareLinkRepository.LastEventUuid); // resolved event, forwarded correctly
    }

    [Fact]
    public async Task NotifyExpenseChangedAsync_LooseExpense_NoOpWithoutEverCallingGetActiveByEvent()
    {
        _expenseRepository.EventUuidResult = null; // loose expense - no owning event

        await CreateNotifier().NotifyExpenseChangedAsync(UserUuid, ExpenseUuid);

        Assert.Equal(0, _broadcaster.PublishUpdatedCalls);
        Assert.False(_shareLinkRepository.GetActiveByEventCalled); // short-circuits before resolving a link at all
    }

    [Fact]
    public async Task NotifyExpenseChangedAsync_EventWithNoActiveLink_NeverPublishes()
    {
        _expenseRepository.EventUuidResult = EventUuid;
        _shareLinkRepository.Active = null;

        await CreateNotifier().NotifyExpenseChangedAsync(UserUuid, ExpenseUuid);

        Assert.Equal(0, _broadcaster.PublishUpdatedCalls);
        Assert.True(_shareLinkRepository.GetActiveByEventCalled); // it WAS resolved, just found nothing active
    }

    [Fact]
    public async Task NotifyExpenseChangedAsync_ExpenseRepositoryThrows_SwallowsLogsAndNeverRethrows()
    {
        _expenseRepository.ThrowOnGetEventUuid = true;

        await CreateNotifier().NotifyExpenseChangedAsync(UserUuid, ExpenseUuid); // must not throw

        Assert.Equal(0, _broadcaster.PublishUpdatedCalls);
        Assert.True(_logger.HasWarning);
    }

    // ---------------------------- Fakes ----------------------------

    private sealed class FakeEventShareLinkRepository : IEventShareLinkRepository
    {
        public EventShareLink? Active { get; set; }
        public bool ThrowOnGetActive { get; set; }
        public bool GetActiveByEventCalled { get; private set; }
        public string? LastUserUuid { get; private set; }
        public string? LastEventUuid { get; private set; }

        public Task<EventShareLink?> GetActiveByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            GetActiveByEventCalled = true;
            LastUserUuid = userUuid;
            LastEventUuid = eventUuid;
            if (ThrowOnGetActive)
                throw new InvalidOperationException("simulated repository failure");
            return Task.FromResult(Active);
        }

        public Task<EventShareLink> CreateAsync(string userUuid, string eventUuid, string token, DateTime expiresAt,
            string? bankAccountUuid, string? bankBin, string? bankName, string? accountNumber, string? accountHolderName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(bool Revoked, string? Token)> RevokeActiveByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EventShareLink?> GetByTokenAsync(string token, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeExpenseRepository : IExpenseRepository
    {
        public string? EventUuidResult { get; set; }
        public bool ThrowOnGetEventUuid { get; set; }

        public Task<string?> GetEventUuidAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default)
        {
            if (ThrowOnGetEventUuid)
                throw new InvalidOperationException("simulated repository failure");
            return Task.FromResult(EventUuidResult);
        }

        public Task<IReadOnlyList<Expense>> ListByUserAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Expense?> GetByUuidAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Expense>> ListDetailedByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteResult<Expense>> CreateAsync(string userUuid, CreateExpenseData data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteResult<Expense>> UpdateGeneralInfoAsync(string userUuid, string expenseUuid, UpdateExpenseData data, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteStatus> DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteStatus> SetSettledAsync(string userUuid, string expenseUuid, bool isSettled, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteResult<Expense>> AssignEventAsync(string userUuid, string expenseUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseWriteStatus> RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountByUserInRangeAsync(string userUuid, DateTime fromUtcInclusive, DateTime toUtcExclusive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<Expense> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();
        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeStreamBroadcaster : IEventShareStreamBroadcaster
    {
        public int PublishUpdatedCalls { get; private set; }
        public string? LastUpdatedToken { get; private set; }

        public IEventShareStreamSubscription Subscribe(string token) => throw new NotSupportedException();

        public void PublishUpdated(string token)
        {
            PublishUpdatedCalls++;
            LastUpdatedToken = token;
        }

        public void PublishRevoked(string token) => throw new NotSupportedException();
        public void PublishExpired(string token) => throw new NotSupportedException();
    }
}
