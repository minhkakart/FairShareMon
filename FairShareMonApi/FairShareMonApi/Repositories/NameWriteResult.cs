namespace FairShareMonApi.Repositories;

/// <summary>Outcome of an atomic name-scoped create/update (uniqueness + reactivation) write.</summary>
public enum NameWriteStatus
{
    /// <summary>A brand-new row was inserted.</summary>
    Created,

    /// <summary>A soft-deleted row with the same name was revived instead of inserting a duplicate.</summary>
    Reactivated,

    /// <summary>An existing row was updated in place.</summary>
    Updated,

    /// <summary>An active row with the same name already exists - the write was rejected.</summary>
    NameDuplicate,

    /// <summary>The target row (or owning user) was not found within the caller's scope.</summary>
    NotFound
}

/// <summary>
/// Result of an atomic create/update that enforces "unique active name per ledger" and, for creates,
/// reactivation-on-name-reuse. Carries the affected entity on success, otherwise the failing status.
/// </summary>
public sealed record NameWriteResult<T>(NameWriteStatus Status, T? Entity) where T : class
{
    public static NameWriteResult<T> Created(T entity) => new(NameWriteStatus.Created, entity);

    public static NameWriteResult<T> Reactivated(T entity) => new(NameWriteStatus.Reactivated, entity);

    public static NameWriteResult<T> Updated(T entity) => new(NameWriteStatus.Updated, entity);

    public static NameWriteResult<T> NameDuplicate() => new(NameWriteStatus.NameDuplicate, null);

    public static NameWriteResult<T> NotFound() => new(NameWriteStatus.NotFound, null);
}
