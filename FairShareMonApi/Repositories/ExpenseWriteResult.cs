namespace FairShareMonApi.Repositories;

/// <summary>
/// Outcome of an atomic expense/share write. Carries the link-invalid + business-rule variants back
/// to the service (mapped there to the 6xxx/7xxx <c>ErrorException</c>s) instead of throwing across
/// the transaction boundary (mirrors <see cref="NameWriteStatus"/>).
/// </summary>
public enum ExpenseWriteStatus
{
    /// <summary>The write succeeded.</summary>
    Success,

    /// <summary>The expense (or owning user) was not found within the caller's scope (6000).</summary>
    ExpenseNotFound,

    /// <summary>The chosen payer member is foreign or soft-deleted (6001).</summary>
    PayerInvalid,

    /// <summary>The chosen category is foreign or soft-deleted (6002).</summary>
    CategoryInvalid,

    /// <summary>A chosen tag is foreign or soft-deleted (6003).</summary>
    TagInvalid,

    /// <summary>The share was not found within the caller's scope (7000).</summary>
    ShareNotFound,

    /// <summary>A share's member is foreign or soft-deleted (7001).</summary>
    ShareMemberInvalid,

    /// <summary>Attempt to delete, or change the member of, the owner-representative's share (7002).</summary>
    OwnerRepresentativeShareNotDeletable,

    /// <summary>Two shares for the same member in one expense (7003).</summary>
    DuplicateShareMember
}

/// <summary>
/// Result of an atomic expense/share write: the affected entity on success, otherwise the failing
/// status. The transaction is rolled back (<c>NoCommit</c>) on any non-<see cref="ExpenseWriteStatus.Success"/>
/// status, so a rejected write leaves no row and no audit (§3.8).
/// </summary>
public sealed record ExpenseWriteResult<T>(ExpenseWriteStatus Status, T? Entity) where T : class
{
    public static ExpenseWriteResult<T> Success(T entity) => new(ExpenseWriteStatus.Success, entity);

    public static ExpenseWriteResult<T> Fail(ExpenseWriteStatus status) => new(status, null);
}

/// <summary>Repository-layer input for creating an expense atomically with its shares (§4.5).</summary>
public sealed record CreateExpenseData(
    string Name,
    string? Description,
    DateTime ExpenseTime,
    string? PayerMemberUuid,
    string? CategoryUuid,
    IReadOnlyList<string> TagUuids,
    IReadOnlyList<CreateShareData> Shares);

/// <summary>Repository-layer input for one share when creating an expense.</summary>
public sealed record CreateShareData(string MemberUuid, decimal Amount, string? Note);

/// <summary>Repository-layer input for updating an expense's general info (tag set is a full replace, OQ18).</summary>
public sealed record UpdateExpenseData(
    string Name,
    string? Description,
    DateTime ExpenseTime,
    string? PayerMemberUuid,
    string? CategoryUuid,
    IReadOnlyList<string> TagUuids);

/// <summary>Repository-layer input for adding/updating an individual share.</summary>
public sealed record ShareData(string MemberUuid, decimal Amount, string? Note);
