using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>The audited entity kind (OQ9).</summary>
public enum AuditEntityType
{
    Expense = 0,
    Share = 1
}

/// <summary>The audited operation (OQ9).</summary>
public enum AuditAction
{
    Create = 0,
    Update = 1,
    Delete = 2
}

/// <summary>
/// Immutable change record over expenses and shares (nhật ký thay đổi, table <c>audit_logs</c>) -
/// The-ideal.md §3.8. One table for both entity kinds (OQ9): <see cref="EntityUuid"/> and
/// <see cref="ExpenseUuid"/> are stored as <b>plain values with no FK</b> so the log survives the
/// hard-delete of its expense/share (OQ3); <see cref="ExpenseUuid"/> is set on both expense and share
/// rows so the per-expense history groups even after the expense is gone. Snapshots
/// (<see cref="BeforeData"/>/<see cref="AfterData"/>) embed denormalized display names alongside uuids
/// so history stays readable after renames/deletes (OQ10). No-op edits (equal snapshots) produce no
/// row; the settled toggle produces no row (OQ11). Never updated or deleted by any code path;
/// implements <see cref="IEntity"/> for the uuid/created_at conventions - <see cref="UpdatedAt"/> is
/// present but inert.
/// </summary>
public partial class AuditLog : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>The user who performed the change (FK -> <c>users.id</c>, cascade delete).</summary>
    public ulong ActorUserId { get; set; }

    /// <summary>Which kind of entity changed (Expense | Share).</summary>
    public AuditEntityType EntityType { get; set; }

    /// <summary>UUID of the changed entity (expense or share) - a plain value, no FK (survives hard-delete).</summary>
    public required string EntityUuid { get; set; }

    /// <summary>UUID of the owning expense (same as <see cref="EntityUuid"/> for expense rows) - a plain value, no FK.</summary>
    public required string ExpenseUuid { get; set; }

    /// <summary>Create | Update | Delete.</summary>
    public AuditAction Action { get; set; }

    /// <summary>JSON snapshot before the change; null on Create.</summary>
    public string? BeforeData { get; set; }

    /// <summary>JSON snapshot after the change; null on Delete.</summary>
    public string? AfterData { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Present for convention; never written after insert (the log is immutable).</summary>
    public DateTime UpdatedAt { get; set; }

    public User ActorUser { get; set; } = null!;
}
