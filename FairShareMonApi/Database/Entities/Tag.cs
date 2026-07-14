using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Free-form classification tag (nhãn, table <c>tags</c>) for expenses (The-ideal.md §3.4, §5).
/// Name-only (no color/icon/default). Belongs to exactly one <see cref="User"/> (<c>user_id</c>).
/// Unique active name per ledger (enforced in application code - MariaDB has no filtered unique
/// index). Soft-deletable (<see cref="IsDeleted"/>): a deleted tag disappears from new-data
/// selection but historical expenses keep the link; creating a tag whose name reuses a soft-deleted
/// tag's name reactivates the old row instead of duplicating (The-ideal.md §4.7/§4.8).
/// </summary>
public partial class Tag : IEntity, IEntityDeletable
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Owning user (FK -> <c>users.id</c>, cascade delete).</summary>
    public ulong UserId { get; set; }

    /// <summary>Display name, 1-100 chars. Unique among the user's active tags (case/accent-insensitive).</summary>
    public required string Name { get; set; }

    /// <summary>Soft-delete flag; deleted tags are excluded by default by <c>BaseRepository.Query</c>.</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
