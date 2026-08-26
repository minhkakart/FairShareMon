using DiDecoration.Attributes;
using FairShareMonApi.Repositories;

namespace FairShareMonApi.Services.Api.Share;

/// <summary>
/// Post-commit notify seam for the three settled-toggle mutation services
/// (planning/public-share-sse-updates.md Step 3). Best-effort and never throws (Decision 3): a failure
/// to resolve the active link or to publish must never turn an already-committed settled toggle into a
/// failed HTTP request.
/// </summary>
public interface IEventShareUpdateNotifier
{
    /// <summary>The event's UUID is already known to the caller (EventsService). No-op if it has no active link.</summary>
    Task NotifyEventChangedAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>The caller only knows an expenseUuid (ExpensesService/SharesService). Resolves the owning event
    /// first (no-op for a loose expense), then behaves like <see cref="NotifyEventChangedAsync"/>.</summary>
    Task NotifyExpenseChangedAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IEventShareUpdateNotifier))]
public sealed class EventShareUpdateNotifier(
    IEventShareLinkRepository shareLinkRepository,
    IExpenseRepository expenseRepository,
    IEventShareStreamBroadcaster broadcaster,
    ILogger<EventShareUpdateNotifier> logger) : IEventShareUpdateNotifier
{
    public async Task NotifyEventChangedAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
    {
        try
        {
            var active = await shareLinkRepository.GetActiveByEventAsync(userUuid, eventUuid, cancellationToken);
            if (active is not null)
                broadcaster.PublishUpdated(active.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Share-link update notify failed for event {EventUuid}; the underlying settled toggle already committed.", eventUuid);
        }
    }

    public async Task NotifyExpenseChangedAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default)
    {
        try
        {
            var eventUuid = await expenseRepository.GetEventUuidAsync(userUuid, expenseUuid, cancellationToken);
            if (eventUuid is null)
                return; // Loose expense - nothing to notify.

            await NotifyEventChangedAsync(userUuid, eventUuid, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Share-link update notify failed for expense {ExpenseUuid}; the underlying settled toggle already committed.", expenseUuid);
        }
    }
}
