using DiDecoration.Attributes;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Events;
using FairShareMonApi.Services.Api.Shares;
using FairShareMonApi.Utils;
using Microsoft.Extensions.Logging;

namespace FairShareMonApi.Services.Api.BankCallbacks;

/// <summary>
/// The orchestrator (planning/bank-callback-settlement.md Step 5): idempotency dedup, correlation-code
/// resolution, exact-amount confirmation, then a single call into the EXISTING, unmodified settlement
/// surface (<see cref="ISharesService.SetSettledAsync"/> / <see cref="IEventsService.SetMemberSettledAsync"/>)
/// - exactly as if the owner had clicked the manual "đã trả" toggle. Invents NO new settlement math.
/// </summary>
public interface IBankCallbackService
{
    Task<BankCallbackOutcome> ProcessAsync(
        string providerKey,
        BankTransactionEvent transactionEvent,
        string rawPayload,
        CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IBankCallbackService))]
public sealed class BankCallbackService(
    IBankTransactionCallbackRepository callbackRepository,
    IQrCorrelationCodeRepository correlationRepository,
    ISharesService sharesService,
    IEventsService eventsService,
    ILogger<BankCallbackService> logger) : IBankCallbackService
{
    public async Task<BankCallbackOutcome> ProcessAsync(
        string providerKey,
        BankTransactionEvent transactionEvent,
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        // Step 1: idempotency - a retried/duplicated webhook must not reprocess.
        var existing = await callbackRepository.FindByProviderTransactionAsync(providerKey, transactionEvent.ProviderTransactionId, cancellationToken);
        if (existing is not null)
            return existing.Outcome;

        // Step 2: outbound (or otherwise non-incoming) transactions are never a settlement target.
        if (!transactionEvent.IsIncoming)
            return await RecordAsync(providerKey, transactionEvent, rawPayload, correlationCodeId: null, resolvedUserId: null, BankCallbackOutcome.Ignored, failureNote: null, appliedAt: null, cancellationToken);

        // Step 3: no extractable code - nothing to resolve.
        if (string.IsNullOrWhiteSpace(transactionEvent.ExtractedCode))
            return await RecordAsync(providerKey, transactionEvent, rawPayload, correlationCodeId: null, resolvedUserId: null, BankCallbackOutcome.UnmatchedCode, failureNote: null, appliedAt: null, cancellationToken);

        // Step 4: resolve the code to its LIVE current target.
        var target = await correlationRepository.ResolveCurrentTargetAsync(transactionEvent.ExtractedCode, cancellationToken);
        if (target is null)
            return await RecordAsync(providerKey, transactionEvent, rawPayload, correlationCodeId: null, resolvedUserId: null, BankCallbackOutcome.UnmatchedCode, failureNote: null, appliedAt: null, cancellationToken);

        // Step 5 (OQ6): soft/logged-only destination cross-check - never blocks.
        if (!string.IsNullOrWhiteSpace(transactionEvent.DestinationAccountNumber))
        {
            logger.LogInformation(
                "Bank callback {ProviderKey}/{ProviderTransactionId} destination account {DestinationAccountNumber} was not cross-checked against a stored account (no destination captured on the correlation target).",
                providerKey, transactionEvent.ProviderTransactionId, transactionEvent.DestinationAccountNumber);
        }

        // Step 6: already settled - an idempotent no-op (a retried/duplicate transfer for an already-cleared target).
        if (target.IsAlreadySettled)
            return await RecordAsync(providerKey, transactionEvent, rawPayload, target.CorrelationCodeId, target.UserId, BankCallbackOutcome.AlreadySettledNoOp, failureNote: null, appliedAt: null, cancellationToken);

        // Step 7 (OQ4): exact match required, re-resolved live.
        if (transactionEvent.Amount != target.CurrentExpectedAmount)
            return await RecordAsync(providerKey, transactionEvent, rawPayload, target.CorrelationCodeId, target.UserId, BankCallbackOutcome.AmountMismatch, failureNote: null, appliedAt: null, cancellationToken);

        // Step 8: apply - the ONE call into the existing, unmodified settlement surface.
        try
        {
            if (target.Kind == CorrelationTargetKind.Share)
            {
                await sharesService.SetSettledAsync(
                    target.UserUuid, target.ExpenseUuid!, target.ShareUuid!,
                    new SetSettledRequest { IsSettled = true }, cancellationToken);
            }
            else
            {
                await eventsService.SetMemberSettledAsync(
                    target.UserUuid, target.EventUuid!, target.MemberUuid,
                    new SetSettledRequest { IsSettled = true }, cancellationToken);
            }
        }
        catch (ErrorException ex)
        {
            // A resource-owned race (e.g. the target was deleted between step 4 and step 8) - logged and
            // held back, never propagated as a 500 (Decision Log entry 6).
            logger.LogWarning(ex, "Bank callback {ProviderKey}/{ProviderTransactionId} failed to apply the settle toggle for correlation code {ExtractedCode}.",
                providerKey, transactionEvent.ProviderTransactionId, transactionEvent.ExtractedCode);
            return await RecordAsync(providerKey, transactionEvent, rawPayload, target.CorrelationCodeId, target.UserId, BankCallbackOutcome.VerificationFailed, ex.Message, appliedAt: null, cancellationToken);
        }

        // Step 9.
        return await RecordAsync(providerKey, transactionEvent, rawPayload, target.CorrelationCodeId, target.UserId, BankCallbackOutcome.Applied, failureNote: null, AppDateTime.Now, cancellationToken);
    }

    private async Task<BankCallbackOutcome> RecordAsync(
        string providerKey,
        BankTransactionEvent transactionEvent,
        string rawPayload,
        ulong? correlationCodeId,
        ulong? resolvedUserId,
        BankCallbackOutcome outcome,
        string? failureNote,
        DateTime? appliedAt,
        CancellationToken cancellationToken)
    {
        var data = new BankTransactionCallbackData(
            providerKey,
            transactionEvent.ProviderTransactionId,
            transactionEvent.IsIncoming,
            transactionEvent.Amount,
            transactionEvent.BankBin,
            transactionEvent.DestinationAccountNumber,
            transactionEvent.Content,
            transactionEvent.ExtractedCode,
            transactionEvent.TransactionAt,
            rawPayload,
            correlationCodeId,
            resolvedUserId,
            outcome,
            failureNote,
            appliedAt);

        var recorded = await callbackRepository.RecordAsync(data, cancellationToken);
        return recorded.Outcome;
    }
}
