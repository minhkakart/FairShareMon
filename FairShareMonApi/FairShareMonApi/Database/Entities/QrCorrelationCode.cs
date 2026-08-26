using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// A short, unique correlation code embedded into a transfer memo at QR-generation time (table
/// <c>qr_correlation_codes</c>, planning/bank-callback-settlement.md), mapping <c>code -&gt; (user,
/// event?, member, expense?, expected amount)</c>. Found-or-created per exact
/// <c>(UserId, EventId, MemberId, ExpenseId, ExpectedAmountSnapshot)</c> tuple (OQ2) so repeated,
/// never-cached QR views/regenerations reuse the same code instead of growing this table unboundedly.
/// <see cref="ExpectedAmountSnapshot"/> is <b>display/debug only</b> - the bank-callback applier always
/// re-resolves the CURRENT expected amount live (mirrors event-expense-settlement-sync's own "recompute
/// live" precedent) rather than trusting a possibly stale value. <see cref="ExpiresAt"/> is a generous
/// 90-day TTL (OQ2): an expired code simply degrades to "unmatched" at apply time, never blocking
/// anything. When <see cref="ExpenseId"/> is set the target is an individual <see cref="Share"/>
/// (resolved live via <c>Expense.Shares</c>); when null the target is the member's per-event net
/// clearance flag (<see cref="EventMemberSettlement"/>).
/// </summary>
public partial class QrCorrelationCode : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Owning user (FK -> <c>users.id</c>, cascade delete).</summary>
    public ulong UserId { get; set; }

    /// <summary>Owning event, when the target is an event-level net clearance (FK -> <c>events.id</c>, nullable, cascade delete).</summary>
    public ulong? EventId { get; set; }

    /// <summary>The billed member (FK -> <c>members.id</c>, restrict).</summary>
    public ulong MemberId { get; set; }

    /// <summary>Owning expense, when the target is an individual share (FK -> <c>expenses.id</c>, nullable, cascade delete).</summary>
    public ulong? ExpenseId { get; set; }

    /// <summary>The short, unique, memo-safe code (OQ1: "FSM" + 6 chars from an unambiguous alphabet).</summary>
    public required string Code { get; set; }

    /// <summary>Display/debug-only snapshot of the expected amount at generation time. The applier never trusts this - it always re-resolves the CURRENT amount live.</summary>
    public decimal ExpectedAmountSnapshot { get; set; }

    /// <summary>Generous TTL (90 days, OQ2). Null is not used in practice - every insert sets one. An expired code degrades to "unmatched", never blocking.</summary>
    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public Event? Event { get; set; }

    public Member Member { get; set; } = null!;

    public Expense? Expense { get; set; }
}
