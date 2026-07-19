using DiDecoration.Attributes;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Data access for <see cref="Event"/> rows. Every read/write is resource-owned: scoped by the owning
/// user's UUID so another user's events are invisible (an ownership miss yields null/EventNotFound,
/// never the row). Writes are single <c>ExecuteTransactionAsync</c> blocks with <c>NoCommit()</c> on
/// failure (§4.5). Events are hard-deleted (not <c>IEntityDeletable</c>) and only while OPEN; deleting
/// an event loosens its expenses via the FK <c>ON DELETE SET NULL</c> (OQ2). Closing is one-way (OQ3).
/// The date range is normalized to a whole-day-inclusive window in the request timezone, then stored as
/// UTC bounds (planning/timezone-aware-datetimes.md D3, refines the M6 OQ1 UTC-day caveat).
/// </summary>
public interface IEventRepository : IBaseRepository, IQueryRepository<Event>
{
    /// <summary>Resource-owned list; optional closed filter; sorted start_date DESC then created_at DESC (OQ10). Includes Expenses (+ their Shares) for the derived expenseCount, totalAdvanced and effective updatedAt.</summary>
    Task<IReadOnlyList<Event>> ListByUserAsync(string userUuid, EventFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Resource-owned lookup by UUID (includes Expenses + their Shares for the derived expenseCount, totalAdvanced and effective updatedAt). Null on an ownership miss.</summary>
    Task<Event?> GetByUuidAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>Atomic create; normalizes the range (OQ1); is_closed = false. Unknown user -&gt; EventNotFound.</summary>
    Task<EventWriteResult<Event>> CreateAsync(string userUuid, CreateEventData data, CancellationToken cancellationToken = default);

    /// <summary>Edits info + range while OPEN; closed -&gt; EventClosed; a new range that would exclude an assigned expense -&gt; RangeExcludesAssignedExpenses (OQ7); miss -&gt; EventNotFound.</summary>
    Task<EventWriteResult<Event>> UpdateAsync(string userUuid, string eventUuid, UpdateEventData data, CancellationToken cancellationToken = default);

    /// <summary>One-way close (OQ8: no preconditions); already closed -&gt; EventClosed; miss -&gt; EventNotFound.</summary>
    Task<EventWriteStatus> CloseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes while OPEN (expenses go loose via ON DELETE SET NULL); closed -&gt; EventClosed; miss -&gt; EventNotFound.</summary>
    Task<EventWriteStatus> DeleteAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default);

    /// <summary>DB-side count of the user's OPEN events (<c>is_closed = false</c>); closed events never count (M10 tier limit, OQ2/§4.9).</summary>
    Task<int> CountOpenByUserAsync(string userUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IEventRepository))]
public sealed class EventRepository(AppDbContext dbContext) : BaseRepository(dbContext), IEventRepository
{
    public IQueryable<Event> Query(bool tracking = false, bool includeDeleted = false) =>
        Query<Event>(tracking, includeDeleted);

    public Task<IReadOnlyList<Event>> ListByUserAsync(string userUuid, EventFilter filter, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var query = Query().Where(evt => evt.User.Uuid == userUuid);

            if (filter.Closed.HasValue)
                query = query.Where(evt => evt.IsClosed == filter.Closed.Value);

            var events = await query
                .Include(evt => evt.Expenses)
                    .ThenInclude(exp => exp.Shares)
                .AsSplitQuery()
                .OrderByDescending(evt => evt.StartDate)
                .ThenByDescending(evt => evt.CreatedAt)
                .ToListAsync(ct);
            return (IReadOnlyList<Event>)events;
        }, cancellationToken);

    public Task<Event?> GetByUuidAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .Include(evt => evt.Expenses)
                .ThenInclude(exp => exp.Shares)
            .FirstOrDefaultAsync(evt => evt.Uuid == eventUuid && evt.User.Uuid == userUuid, ct), cancellationToken);

    public Task<int> CountOpenByUserAsync(string userUuid, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync((_, ct) => Query()
            .Where(evt => evt.User.Uuid == userUuid && !evt.IsClosed)
            .CountAsync(ct), cancellationToken);

    public Task<EventWriteResult<Event>> CreateAsync(string userUuid, CreateEventData data, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var userId = await ResolveUserIdAsync(db, userUuid, cancellationToken);
            if (userId is null)
            {
                transaction.NoCommit();
                return EventWriteResult<Event>.Fail(EventWriteStatus.EventNotFound);
            }

            var evt = new Event
            {
                UserId = userId.Value,
                Name = data.Name,
                Description = data.Description,
                StartDate = NormalizeStart(data.StartDate, data.Zone),
                EndDate = NormalizeEnd(data.EndDate, data.Zone),
                IsClosed = false
            };
            db.Events.Add(evt);
            return EventWriteResult<Event>.Success(evt);
        }, cancellationToken);

    public Task<EventWriteResult<Event>> UpdateAsync(string userUuid, string eventUuid, UpdateEventData data, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var evt = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == eventUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (evt is null)
            {
                transaction.NoCommit();
                return EventWriteResult<Event>.Fail(EventWriteStatus.EventNotFound);
            }

            // A closed event cannot be edited (§5 lock, OQ11).
            if (evt.IsClosed)
            {
                transaction.NoCommit();
                return EventWriteResult<Event>.Fail(EventWriteStatus.EventClosed);
            }

            var newStart = NormalizeStart(data.StartDate, data.Zone);
            var newEnd = NormalizeEnd(data.EndDate, data.Zone);

            // OQ7: block the edit if any already-assigned expense would fall outside the new range.
            var wouldExclude = await db.Expenses.AsNoTracking()
                .AnyAsync(expense => expense.EventId == evt.Id
                    && (expense.ExpenseTime < newStart || expense.ExpenseTime > newEnd), cancellationToken);
            if (wouldExclude)
            {
                transaction.NoCommit();
                return EventWriteResult<Event>.Fail(EventWriteStatus.RangeExcludesAssignedExpenses);
            }

            evt.Name = data.Name;
            evt.Description = data.Description;
            evt.StartDate = newStart;
            evt.EndDate = newEnd;
            return EventWriteResult<Event>.Success(evt);
        }, cancellationToken);

    public Task<EventWriteStatus> CloseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (_, transaction) =>
        {
            var evt = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == eventUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (evt is null)
            {
                transaction.NoCommit();
                return EventWriteStatus.EventNotFound;
            }

            // One-way: re-closing an already-closed event is rejected (OQ11).
            if (evt.IsClosed)
            {
                transaction.NoCommit();
                return EventWriteStatus.EventClosed;
            }

            evt.IsClosed = true;
            evt.ClosedAt = AppDateTime.Now;
            return EventWriteStatus.Success;
        }, cancellationToken);

    public Task<EventWriteStatus> DeleteAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var evt = await Query(tracking: true)
                .FirstOrDefaultAsync(existing => existing.Uuid == eventUuid && existing.User.Uuid == userUuid, cancellationToken);
            if (evt is null)
            {
                transaction.NoCommit();
                return EventWriteStatus.EventNotFound;
            }

            // Delete only while OPEN (§5 lock, OQ3).
            if (evt.IsClosed)
            {
                transaction.NoCommit();
                return EventWriteStatus.EventClosed;
            }

            // Hard delete: the FK ON DELETE SET NULL loosens this event's expenses (OQ2).
            db.Events.Remove(evt);
            return EventWriteStatus.Success;
        }, cancellationToken);

    /// <summary>
    /// Normalizes the range start to 00:00:00.000000 of its calendar day IN THE REQUEST ZONE, then
    /// converts that local midnight to the UTC instant stored/compared against (D3). Within-range checks
    /// stay raw UTC-instant compares - the bounds already encode the zone.
    /// </summary>
    private static DateTime NormalizeStart(DateTime value, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(value), zone);
        var startLocal = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(startLocal, zone);
    }

    /// <summary>
    /// Normalizes the range end to 23:59:59.999999 of its calendar day IN THE REQUEST ZONE (next local
    /// midnight minus 1 microsecond, staying within datetime(6) precision), then converts to the stored
    /// UTC instant (D3).
    /// </summary>
    private static DateTime NormalizeEnd(DateTime value, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(value), zone);
        var endLocal = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified).AddDays(1).AddTicks(-10);
        return TimeZoneInfo.ConvertTimeToUtc(endLocal, zone);
    }

    /// <summary>Guards <c>ConvertTimeFromUtc</c>, which rejects a <c>Kind.Local</c> source.</summary>
    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static Task<ulong?> ResolveUserIdAsync(AppDbContext db, string userUuid, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(user => user.Uuid == userUuid)
            .Select(user => (ulong?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
