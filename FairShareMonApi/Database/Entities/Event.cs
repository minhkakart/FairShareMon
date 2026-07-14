using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// A spending event (đợt chi tiêu, table <c>events</c>) - The-ideal.md §2, §3.6. Groups zero or more
/// <see cref="Expense"/> rows over a whole-day-inclusive UTC date range (<see cref="StartDate"/> at
/// 00:00:00, <see cref="EndDate"/> at 23:59:59.999999, OQ1), backed by the DB CHECK
/// <c>ck_events_date_range</c> (<c>end_date &gt;= start_date</c>). Belongs to exactly one
/// <see cref="User"/> (<c>user_id</c>). Has an OPEN/CLOSED lifecycle (<see cref="IsClosed"/> +
/// <see cref="ClosedAt"/>, mirroring <c>is_settled</c>/<c>settled_at</c>); closing is one-way and
/// never automatic (§5). <b>Hard-deleted</b> (not <see cref="IEntityDeletable"/>, OQ3) and only while
/// OPEN: its expenses are not deleted but go loose (<c>event_id</c> -&gt; null via
/// <c>ON DELETE SET NULL</c>, OQ2). Event membership is not audited (OQ6).
/// </summary>
public partial class Event : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Owning user (FK -> <c>users.id</c>, cascade delete).</summary>
    public ulong UserId { get; set; }

    /// <summary>Display name, 1-200 chars (OQ9).</summary>
    public required string Name { get; set; }

    /// <summary>Optional free-form description, max 1000 chars (OQ9).</summary>
    public string? Description { get; set; }

    /// <summary>Inclusive range start, normalized to 00:00:00.000000 UTC on write (OQ1).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Inclusive range end, normalized to 23:59:59.999999 UTC on write (OQ1).</summary>
    public DateTime EndDate { get; set; }

    /// <summary>Closed flag (đã chốt): a closed event rejects all writes to its expenses/shares except the settled flag (§4.4). Closing is one-way (OQ3/OQ11).</summary>
    public bool IsClosed { get; set; }

    /// <summary>When the event was closed. Null while OPEN (OQ9).</summary>
    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    /// <summary>The event's expenses; loosened (<c>event_id</c> -&gt; null) when the event is deleted (OQ2).</summary>
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
