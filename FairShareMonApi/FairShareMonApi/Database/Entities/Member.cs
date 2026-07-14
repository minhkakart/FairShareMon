using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Ledger participant in cost-splitting (table <c>members</c>) - named and managed by the owning
/// user, with NO account of their own. Belongs to exactly one <see cref="User"/> (<c>user_id</c>).
/// Every ledger always has exactly one owner-representative member
/// (<see cref="IsOwnerRepresentative"/>), created atomically on registration and backfilled for
/// pre-existing users. Soft-deletable (<see cref="IsDeleted"/>): a deleted member disappears from
/// new-data selection lists but all historical data still shows the member's name
/// (The-ideal.md §2, §3.2, §4.7/§4.8).
/// </summary>
public partial class Member : IEntity, IEntityDeletable
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Owning user (FK -> <c>users.id</c>, cascade delete).</summary>
    public ulong UserId { get; set; }

    /// <summary>Display name, 1-100 chars. Free-form; duplicates allowed (no uniqueness).</summary>
    public required string Name { get; set; }

    /// <summary>True for the single owner-representative member ("thành viên đại diện chủ sổ"):
    /// renamable but not deletable.</summary>
    public bool IsOwnerRepresentative { get; set; }

    /// <summary>Soft-delete flag; deleted members are excluded by default by <c>BaseRepository.Query</c>.</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
