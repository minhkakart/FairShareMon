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

    /// <summary>
    /// Per-share settled flag (đã trả), Layer A of the settled-per-member feature (§3.5, §6). Payment
    /// metadata only - never changes <see cref="Amount"/>. Reconciled with the whole-expense
    /// <see cref="Expense.IsSettled"/> (settled-per-member OQ3): the expense is settled iff every
    /// "billable" share (<c>Amount &gt; 0</c> and member ≠ payer) is settled. Not audited (OQ10).
    /// </summary>
    public bool IsSettled { get; set; }

    /// <summary>When this share was last toggled settled (set on true, cleared on false). Null when never settled (settled-per-member OQ2).</summary>
    public DateTime? SettledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Expense Expense { get; set; } = null!;

    public Member Member { get; set; } = null!;
}
