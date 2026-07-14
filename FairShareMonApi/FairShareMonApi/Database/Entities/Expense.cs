using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// A single expenditure entry (phiếu chi tiêu, table <c>expenses</c>) - The-ideal.md §2, §3.5.
/// Belongs to exactly one <see cref="User"/> (<c>user_id</c>). Has a payer (a <see cref="Member"/>),
/// a <see cref="Category"/>, zero or more tags (via <see cref="ExpenseTag"/>), and a list of
/// <see cref="Share"/> rows; the expense total is definitionally the sum of its shares (§2, OQ1) -
/// there is no stored total column. <b>Hard-deleted</b> (not <see cref="IEntityDeletable"/>, OQ3):
/// deleting an expense physically removes it and cascades its shares + expense_tags; the immutable
/// <see cref="AuditLog"/> preserves the before-state. The <see cref="IsSettled"/> flag (đã trả) is
/// payment metadata, not an expenditure figure (§3.5).
/// </summary>
public partial class Expense : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Owning user (FK -> <c>users.id</c>, cascade delete).</summary>
    public ulong UserId { get; set; }

    /// <summary>Display name, 1-200 chars (OQ16).</summary>
    public required string Name { get; set; }

    /// <summary>Optional free-form description, max 1000 chars (OQ16).</summary>
    public string? Description { get; set; }

    /// <summary>When the expenditure happened (thời điểm chi). No range bounds in M5 (OQ14).</summary>
    public DateTime ExpenseTime { get; set; }

    /// <summary>Paying member (FK -> <c>members.id</c>, required, restrict). Defaults to the owner-rep member (§3.5, OQ4/OQ7).</summary>
    public ulong PayerMemberId { get; set; }

    /// <summary>Category (FK -> <c>categories.id</c>, required, restrict). Defaults to the user's default category (§3.5, OQ7).</summary>
    public ulong CategoryId { get; set; }

    /// <summary>Optional owning event (FK -> <c>events.id</c>, nullable, ON DELETE SET NULL). Null = loose (no event). M6 §3.5/§3.6.</summary>
    public ulong? EventId { get; set; }

    /// <summary>Settled flag (đã trả): payment metadata, does not change amounts (§3.5, OQ11/OQ12).</summary>
    public bool IsSettled { get; set; }

    /// <summary>When the expense was last toggled settled (set on true, cleared on false). Null when never settled (OQ12).</summary>
    public DateTime? SettledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public Member PayerMember { get; set; } = null!;

    public Category Category { get; set; } = null!;

    /// <summary>The owning event, or null when the expense is loose (M6, OQ2/OQ14).</summary>
    public Event? Event { get; set; }

    /// <summary>The expense's shares (phần gánh); cascade-deleted with the expense.</summary>
    public ICollection<Share> Shares { get; set; } = new List<Share>();

    /// <summary>Join rows to the expense's tags (via <see cref="ExpenseTag"/>); cascade-deleted with the expense.</summary>
    public ICollection<ExpenseTag> ExpenseTags { get; set; } = new List<ExpenseTag>();
}
