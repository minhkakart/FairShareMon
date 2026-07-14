using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Append-only manual tier grant/revoke record (table <c>tier_grants</c>) - M11 Admin suite, the
/// manual paid-upgrade path (The-ideal.md §3.11). One row per admin grant/revoke action; a
/// <see cref="Action"/> = <c>GRANT</c> row records the offline payment amount and upgrades the user to
/// Premium, a <c>REVOKE</c> row (amount 0) downgrades to Free. This table is the revenue dashboard's
/// sole data source (OQ14); only GRANT rows count as revenue.
/// <para>
/// Consistent with <see cref="AuditLog"/>'s immutable-trail design (OQ5): <see cref="UserId"/> and
/// <see cref="GrantedByUserId"/> are stored as <b>plain values with no navigation FK</b>, and the
/// usernames are <b>denormalized snapshots</b> so the history renders without joining back into
/// <c>users</c> (privacy-safe). Never updated after insert; <see cref="UpdatedAt"/> is present for
/// convention but inert.
/// </para>
/// </summary>
public partial class TierGrant : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Target user (<c>users.id</c>) - a plain value, no FK (immutable trail, OQ5).</summary>
    public ulong UserId { get; set; }

    /// <summary>Denormalized snapshot of the target user's username at grant time.</summary>
    public required string UserUsername { get; set; }

    /// <summary>Resulting tier per <see cref="Constants.UserTiers"/> (PREMIUM on grant, FREE on revoke).</summary>
    public required string Tier { get; set; }

    /// <summary>Action per <see cref="Constants.TierGrantActions"/> (GRANT | REVOKE).</summary>
    public required string Action { get; set; }

    /// <summary>Offline payment amount (>= 0; 0 for a comp grant or a revoke). Never float - <c>decimal(18,2)</c> + DB CHECK.</summary>
    public decimal Amount { get; set; }

    /// <summary>ISO currency code (default VND).</summary>
    public required string Currency { get; set; }

    /// <summary>Optional offline payment reference (bank transfer id, receipt no., ...).</summary>
    public string? Reference { get; set; }

    /// <summary>Optional admin note.</summary>
    public string? Note { get; set; }

    /// <summary>Acting admin (<c>users.id</c>) - a plain value, no FK (immutable trail, OQ5).</summary>
    public ulong GrantedByUserId { get; set; }

    /// <summary>Denormalized snapshot of the acting admin's username.</summary>
    public required string GrantedByUsername { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Present for convention; never written after insert (the trail is immutable).</summary>
    public DateTime UpdatedAt { get; set; }
}
