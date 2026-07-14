using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// One member's borne amount in one expense (phần gánh, table <c>shares</c>) - The-ideal.md §2, §3.5.
/// Belongs to exactly one <see cref="Expense"/> (<c>expense_id</c>, cascade-deleted) and references a
/// single <see cref="Member"/> (<c>member_id</c>). <see cref="Amount"/> is <c>decimal(18,2)</c> with a
/// DB CHECK <c>amount &gt;= 0</c> (§4.3, OQ2). At most one share per member per expense (unique index
/// on <c>(expense_id, member_id)</c>, OQ5). <b>Hard-deleted</b> (not <see cref="IEntityDeletable"/>,
/// OQ3); the immutable <see cref="AuditLog"/> preserves the before-state.
/// </summary>
public partial class Share : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Owning expense (FK -> <c>expenses.id</c>, cascade delete).</summary>
    public ulong ExpenseId { get; set; }

    /// <summary>The bearing member (FK -> <c>members.id</c>, restrict).</summary>
    public ulong MemberId { get; set; }

    /// <summary>Borne amount, <c>decimal(18,2)</c>, non-negative (DB CHECK, §4.3). 0đ is valid.</summary>
    public decimal Amount { get; set; }

    /// <summary>Optional note, max 500 chars (OQ16).</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Expense Expense { get; set; } = null!;

    public Member Member { get; set; } = null!;
}
