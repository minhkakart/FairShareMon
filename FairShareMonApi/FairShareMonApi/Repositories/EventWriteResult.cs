namespace FairShareMonApi.Repositories;

/// <summary>
/// Outcome of an atomic event write. Carries the business-rule variants back to the service (mapped
/// there to the 9xxx <c>ErrorException</c>s) instead of throwing across the transaction boundary
/// (mirrors <see cref="ExpenseWriteStatus"/> / <c>NameWriteStatus</c>).
/// </summary>
public enum EventWriteStatus
{
    /// <summary>The write succeeded.</summary>
    Success,

    /// <summary>The event (or owning user) was not found within the caller's scope (9000).</summary>
    EventNotFound,

    /// <summary>The event is closed, so it cannot be edited/deleted/re-closed (9001).</summary>
    EventClosed,

    /// <summary>The new range would leave an already-assigned expense out of range (9003).</summary>
    RangeExcludesAssignedExpenses
}

/// <summary>
/// Result of an atomic event write: the affected entity on success, otherwise the failing status.
/// The transaction is rolled back (<c>NoCommit</c>) on any non-<see cref="EventWriteStatus.Success"/>
/// status, so a rejected write leaves no row.
/// </summary>
public sealed record EventWriteResult<T>(EventWriteStatus Status, T? Entity) where T : class
{
    public static EventWriteResult<T> Success(T entity) => new(EventWriteStatus.Success, entity);

    public static EventWriteResult<T> Fail(EventWriteStatus status) => new(status, null);
}

/// <summary>
/// Repository-layer input for creating an event. The whole-day range is normalized in the repository
/// in the request timezone <paramref name="Zone"/> (planning/timezone-aware-datetimes.md D3, refines
/// the M6 OQ1 UTC-day caveat), then converted to UTC bounds for storage.
/// </summary>
public sealed record CreateEventData(string Name, string? Description, DateTime StartDate, DateTime EndDate, TimeZoneInfo Zone);

/// <summary>
/// Repository-layer input for updating an event's info. The whole-day range is re-normalized in the
/// request timezone <paramref name="Zone"/> then converted to UTC bounds (D3).
/// </summary>
public sealed record UpdateEventData(string Name, string? Description, DateTime StartDate, DateTime EndDate, TimeZoneInfo Zone);
