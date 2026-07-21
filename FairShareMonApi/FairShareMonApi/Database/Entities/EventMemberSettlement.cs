namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Per-member-per-event net clearance state (Layer B of the settled-per-member feature, §3.7/§6, table
/// <c>event_member_settlements</c>). Records whether a member has cleared their <b>net</b> debt in one
/// event - a net-level fact distinct from the gross per-<see cref="Share"/> settled flag (Layer A): a
/// member who both advanced and owed in the event clears once the net transfer is made, not once every
/// gross share is settled (settled-per-member OQ1a/OQ8a). Drives the balance "outstanding" overlay and
/// the §3.10 per-owing-member event QR. A lightweight state row (like <see cref="ExpenseTag"/>): no
/// surrogate id/uuid, composite PK <c>(event_id, member_id)</c>. Cascade-deleted with the event; the
/// member FK restricts (settled-per-member OQ1a). Not audited (OQ10).
/// </summary>
public partial class EventMemberSettlement
{
    /// <summary>Owning event (FK -> <c>events.id</c>, cascade delete). Part of the composite PK.</summary>
    public ulong EventId { get; set; }

    /// <summary>The cleared member (FK -> <c>members.id</c>, restrict). Part of the composite PK.</summary>
    public ulong MemberId { get; set; }

    /// <summary>True when the member has cleared their net debt in this event (đã trả). Payment metadata only.</summary>
    public bool IsSettled { get; set; }

    /// <summary>When last toggled settled (set on true, cleared on false). Null when not settled.</summary>
    public DateTime? SettledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Event Event { get; set; } = null!;

    public Member Member { get; set; } = null!;
}
