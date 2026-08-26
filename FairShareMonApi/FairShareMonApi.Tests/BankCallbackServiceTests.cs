using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.BankCallbacks;
using FairShareMonApi.Services.Api.Events;
using FairShareMonApi.Services.Api.Shares;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="BankCallbackService"/> (fakes for both repositories + the two settle
/// services, no DB) - planning/bank-callback-settlement.md Step 10. Proves the 9-step orchestration:
/// idempotency replay, the Ignored/UnmatchedCode/AmountMismatch/AlreadySettledNoOp/Applied outcome paths,
/// the exact settle-service call made on a confident match (Share vs EventMember target), the step-order
/// priority (already-settled short-circuits BEFORE the amount check), the OQ6 soft destination check
/// (never blocks), and the resource-owned <c>ErrorException</c> -&gt; <c>VerificationFailed</c> catch (never
/// rethrown).
/// </summary>
public class BankCallbackServiceTests
{
    private const string ProviderKey = "sepay";

    private readonly FakeBankTransactionCallbackRepository _callbacks = new();
    private readonly FakeQrCorrelationCodeRepository _correlationCodes = new();
    private readonly FakeSharesService _sharesService = new();
    private readonly FakeEventsService _eventsService = new();

    private BankCallbackService CreateService() =>
        new(_callbacks, _correlationCodes, _sharesService, _eventsService, NullLogger<BankCallbackService>.Instance);

    private static BankTransactionEvent Event(
        string providerTransactionId = "tx-1",
        bool isIncoming = true,
        decimal amount = 500_000m,
        string content = "FSM8K2QX7 chuyen tien",
        string? extractedCode = "FSM8K2QX7",
        string? destinationAccountNumber = null) =>
        new(providerTransactionId, isIncoming, amount, content, extractedCode, DateTime.UtcNow, BankBin: null, destinationAccountNumber);

    private static CorrelationTarget ShareTarget(
        decimal currentExpectedAmount = 500_000m, bool isAlreadySettled = false, ulong userId = 1, ulong correlationCodeId = 10) =>
        new(correlationCodeId, userId, CorrelationTargetKind.Share, "user-uuid-1", null, "member-uuid-1", "expense-uuid-1", "share-uuid-1", currentExpectedAmount, isAlreadySettled);

    private static CorrelationTarget EventMemberTarget(
        decimal currentExpectedAmount = 500_000m, bool isAlreadySettled = false, ulong userId = 2, ulong correlationCodeId = 20) =>
        new(correlationCodeId, userId, CorrelationTargetKind.EventMember, "user-uuid-2", "event-uuid-1", "member-uuid-2", null, null, currentExpectedAmount, isAlreadySettled);

    // ---- Step 1: idempotency ------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_DuplicateProviderTransaction_ReturnsCachedOutcome_NoReprocessing()
    {
        _callbacks.Seed(ProviderKey, "tx-1", BankCallbackOutcome.Applied);

        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(providerTransactionId: "tx-1"), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.Applied, outcome);
        Assert.Empty(_correlationCodes.ResolveCalls);
        Assert.Empty(_sharesService.Calls);
        Assert.Empty(_eventsService.Calls);
        Assert.Empty(_callbacks.RecordCalls); // no NEW record written - only the pre-check hit
    }

    // ---- Step 2: not incoming ------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_NotIncoming_RecordsIgnored_NoLookups()
    {
        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(isIncoming: false), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.Ignored, outcome);
        Assert.Empty(_correlationCodes.ResolveCalls);
        Assert.Empty(_sharesService.Calls);
        Assert.Empty(_eventsService.Calls);
        var recorded = Assert.Single(_callbacks.RecordCalls);
        Assert.Equal(BankCallbackOutcome.Ignored, recorded.Outcome);
        Assert.Null(recorded.ResolvedUserId);
        Assert.Null(recorded.MatchedCorrelationCodeId);
    }

    // ---- Step 3: no extractable code ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessAsync_BlankOrNullExtractedCode_RecordsUnmatchedCode_ResolvedUserNull(string? extractedCode)
    {
        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(extractedCode: extractedCode), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.UnmatchedCode, outcome);
        Assert.Empty(_correlationCodes.ResolveCalls); // never even attempts a lookup for a blank code
        var recorded = Assert.Single(_callbacks.RecordCalls);
        Assert.Null(recorded.ResolvedUserId);
    }

    // ---- Step 4: unknown/unresolvable code --------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_UnknownCode_RecordsUnmatchedCode_ResolvedUserNull()
    {
        _correlationCodes.Targets["FSMUNKNOWN"] = null;

        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(extractedCode: "FSMUNKNOWN"), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.UnmatchedCode, outcome);
        Assert.Equal("FSMUNKNOWN", Assert.Single(_correlationCodes.ResolveCalls));
        Assert.Empty(_sharesService.Calls);
        Assert.Empty(_eventsService.Calls);
        var recorded = Assert.Single(_callbacks.RecordCalls);
        Assert.Null(recorded.ResolvedUserId);
        Assert.Null(recorded.MatchedCorrelationCodeId);
    }

    // ---- Step 6 vs Step 7 order: already-settled short-circuits BEFORE the amount check -----------------

    [Fact]
    public async Task ProcessAsync_AlreadySettledTarget_RecordsAlreadySettledNoOp_EvenWhenAmountAlsoMismatches()
    {
        var target = ShareTarget(currentExpectedAmount: 500_000m, isAlreadySettled: true);
        _correlationCodes.Targets["FSM8K2QX7"] = target;

        // Amount deliberately mismatched too - proves Step 6 (already-settled) wins over Step 7 (amount).
        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(amount: 1m), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.AlreadySettledNoOp, outcome);
        Assert.Empty(_sharesService.Calls);
        Assert.Empty(_eventsService.Calls);
        var recorded = Assert.Single(_callbacks.RecordCalls);
        Assert.Equal(target.UserId, recorded.ResolvedUserId);
        Assert.Equal(target.CorrelationCodeId, recorded.MatchedCorrelationCodeId);
    }

    // ---- Step 7: amount mismatch (OQ4, exact match required) ---------------------------------------------

    [Theory]
    [InlineData(499_999)] // under
    [InlineData(500_001)] // over
    public async Task ProcessAsync_AmountMismatch_RecordsAmountMismatch_SettleServiceNeverCalled(decimal transferredAmount)
    {
        var target = ShareTarget(currentExpectedAmount: 500_000m, isAlreadySettled: false);
        _correlationCodes.Targets["FSM8K2QX7"] = target;

        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(amount: transferredAmount), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.AmountMismatch, outcome);
        Assert.Empty(_sharesService.Calls);
        Assert.Empty(_eventsService.Calls);
        var recorded = Assert.Single(_callbacks.RecordCalls);
        Assert.Equal(target.UserId, recorded.ResolvedUserId); // the owner CAN see this one (OQ5)
        Assert.Null(recorded.AppliedAt);
    }

    // ---- Step 8: apply (Share target) ---------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ExactMatchShareTarget_CallsSharesServiceExactlyOnce_RecordsApplied()
    {
        var target = ShareTarget(currentExpectedAmount: 500_000m);
        _correlationCodes.Targets["FSM8K2QX7"] = target;

        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(amount: 500_000m), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.Applied, outcome);
        var call = Assert.Single(_sharesService.Calls);
        Assert.Equal(target.UserUuid, call.UserUuid);
        Assert.Equal(target.ExpenseUuid, call.ExpenseUuid);
        Assert.Equal(target.ShareUuid, call.ShareUuid);
        Assert.True(call.IsSettled);
        Assert.Empty(_eventsService.Calls); // the OTHER settle surface must never be touched
        var recorded = Assert.Single(_callbacks.RecordCalls);
        Assert.Equal(BankCallbackOutcome.Applied, recorded.Outcome);
        Assert.NotNull(recorded.AppliedAt);
        Assert.Equal(target.UserId, recorded.ResolvedUserId);
        Assert.Equal(target.CorrelationCodeId, recorded.MatchedCorrelationCodeId);
    }

    // ---- Step 8: apply (EventMember target) -----------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ExactMatchEventMemberTarget_CallsEventsServiceExactlyOnce_RecordsApplied()
    {
        var target = EventMemberTarget(currentExpectedAmount: 500_000m);
        _correlationCodes.Targets["FSM8K2QX7"] = target;

        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(amount: 500_000m), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.Applied, outcome);
        var call = Assert.Single(_eventsService.Calls);
        Assert.Equal(target.UserUuid, call.UserUuid);
        Assert.Equal(target.EventUuid, call.EventUuid);
        Assert.Equal(target.MemberUuid, call.MemberUuid);
        Assert.True(call.IsSettled);
        Assert.Empty(_sharesService.Calls); // the OTHER settle surface must never be touched
        var recorded = Assert.Single(_callbacks.RecordCalls);
        Assert.Equal(BankCallbackOutcome.Applied, recorded.Outcome);
        Assert.NotNull(recorded.AppliedAt);
    }

    // ---- Step 8: a resource-owned race is caught, not rethrown -------------------------------------------

    [Fact]
    public async Task ProcessAsync_SharesServiceThrowsErrorException_RecordsVerificationFailed_NotRethrown()
    {
        var target = ShareTarget(currentExpectedAmount: 500_000m);
        _correlationCodes.Targets["FSM8K2QX7"] = target;
        _sharesService.ThrowOnSetSettled = new ErrorException(ErrorCodes.ShareNotFound, MessageKeys.Error.ShareNotFound);

        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(amount: 500_000m), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.VerificationFailed, outcome); // never propagated as an exception/500
        var recorded = Assert.Single(_callbacks.RecordCalls);
        Assert.Equal(BankCallbackOutcome.VerificationFailed, recorded.Outcome);
        Assert.NotNull(recorded.FailureNote);
        Assert.Null(recorded.AppliedAt);
    }

    [Fact]
    public async Task ProcessAsync_EventsServiceThrowsErrorException_RecordsVerificationFailed_NotRethrown()
    {
        var target = EventMemberTarget(currentExpectedAmount: 500_000m);
        _correlationCodes.Targets["FSM8K2QX7"] = target;
        _eventsService.ThrowOnSetMemberSettled = new ErrorException(ErrorCodes.MemberNotFound, MessageKeys.Error.MemberNotFound);

        var outcome = await CreateService().ProcessAsync(ProviderKey, Event(amount: 500_000m), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.VerificationFailed, outcome);
        var recorded = Assert.Single(_callbacks.RecordCalls);
        Assert.Equal(BankCallbackOutcome.VerificationFailed, recorded.Outcome);
        Assert.NotNull(recorded.FailureNote);
    }

    // ---- Step 5 (OQ6): soft destination cross-check never blocks -----------------------------------------

    [Fact]
    public async Task ProcessAsync_DestinationAccountPresent_NeverBlocksTheApply()
    {
        var target = ShareTarget(currentExpectedAmount: 500_000m);
        _correlationCodes.Targets["FSM8K2QX7"] = target;

        var outcome = await CreateService().ProcessAsync(
            ProviderKey, Event(amount: 500_000m, destinationAccountNumber: "0000000000-not-the-owners-account"), rawPayload: "{}");

        Assert.Equal(BankCallbackOutcome.Applied, outcome); // logged only (OQ6) - never held back
        Assert.Single(_sharesService.Calls);
    }

    // ---- Fakes -----------------------------------------------------------------------------------------

    private sealed class FakeBankTransactionCallbackRepository : IBankTransactionCallbackRepository
    {
        private readonly Dictionary<(string ProviderKey, string ProviderTransactionId), BankTransactionCallback> _existing = new();

        public List<BankTransactionCallbackData> RecordCalls { get; } = [];

        public void Seed(string providerKey, string providerTransactionId, BankCallbackOutcome outcome) =>
            _existing[(providerKey, providerTransactionId)] = new BankTransactionCallback
            {
                ProviderKey = providerKey,
                ProviderTransactionId = providerTransactionId,
                Content = "seeded",
                RawPayload = "{}",
                Outcome = outcome
            };

        public Task<BankTransactionCallback?> FindByProviderTransactionAsync(string providerKey, string providerTransactionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_existing.GetValueOrDefault((providerKey, providerTransactionId)));

        public Task<BankTransactionCallback> RecordAsync(BankTransactionCallbackData data, CancellationToken cancellationToken = default)
        {
            RecordCalls.Add(data);
            var row = new BankTransactionCallback
            {
                ProviderKey = data.ProviderKey,
                ProviderTransactionId = data.ProviderTransactionId,
                Content = data.Content,
                RawPayload = data.RawPayload,
                Outcome = data.Outcome,
                ResolvedUserId = data.ResolvedUserId,
                MatchedCorrelationCodeId = data.MatchedCorrelationCodeId,
                AppliedAt = data.AppliedAt,
                FailureNote = data.FailureNote
            };
            return Task.FromResult(row);
        }

        public Task<(IReadOnlyList<BankTransactionCallback> Items, int Total)> ListByUserAsync(string userUuid, int limit, int offset, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeQrCorrelationCodeRepository : IQrCorrelationCodeRepository
    {
        public Dictionary<string, CorrelationTarget?> Targets { get; } = [];
        public List<string> ResolveCalls { get; } = [];

        public Task<QrCorrelationCode> GetOrCreateAsync(string userUuid, string? eventUuid, string memberUuid, string? expenseUuid, decimal expectedAmount, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CorrelationTarget?> ResolveCurrentTargetAsync(string code, CancellationToken cancellationToken = default)
        {
            ResolveCalls.Add(code);
            return Task.FromResult(Targets.GetValueOrDefault(code));
        }

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record ShareSettledCall(string UserUuid, string ExpenseUuid, string ShareUuid, bool IsSettled);

    private sealed class FakeSharesService : ISharesService
    {
        public List<ShareSettledCall> Calls { get; } = [];
        public ErrorException? ThrowOnSetSettled { get; set; }

        public Task SetSettledAsync(string userUuid, string expenseUuid, string shareUuid, SetSettledRequest request, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSetSettled is not null)
                throw ThrowOnSetSettled;

            Calls.Add(new ShareSettledCall(userUuid, expenseUuid, shareUuid, request.IsSettled));
            return Task.CompletedTask;
        }

        public Task<ShareResponse> AddAsync(string userUuid, string expenseUuid, CreateShareRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShareResponse> UpdateAsync(string userUuid, string expenseUuid, string shareUuid, UpdateShareRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string userUuid, string expenseUuid, string shareUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record MemberSettledCall(string UserUuid, string EventUuid, string MemberUuid, bool IsSettled);

    private sealed class FakeEventsService : IEventsService
    {
        public List<MemberSettledCall> Calls { get; } = [];
        public ErrorException? ThrowOnSetMemberSettled { get; set; }

        public Task SetMemberSettledAsync(string userUuid, string eventUuid, string memberUuid, SetSettledRequest request, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSetMemberSettled is not null)
                throw ThrowOnSetMemberSettled;

            Calls.Add(new MemberSettledCall(userUuid, eventUuid, memberUuid, request.IsSettled));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EventSummaryResponse>> ListAsync(string userUuid, EventFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventResponse> GetAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventResponse> CreateAsync(string userUuid, CreateEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventResponse> UpdateAsync(string userUuid, string eventUuid, UpdateEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CloseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
