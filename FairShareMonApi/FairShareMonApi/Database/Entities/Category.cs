using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Expense category (danh mục chi tiêu, table <c>categories</c>) - carries a chart color and an
/// optional client-mapped icon (The-ideal.md §2, §3.3). Belongs to exactly one <see cref="User"/>
/// (<c>user_id</c>). Every ledger always has exactly one default category
/// (<see cref="IsDefault"/>), seeded on registration and backfilled for pre-existing users; the
/// default is not deletable and is reassigned atomically. Unique active name per ledger (enforced in
/// application code - MariaDB has no filtered unique index). Soft-deletable
/// (<see cref="IsDeleted"/>): a deleted category disappears from new-data selection but historical
/// expenses keep the link (The-ideal.md §4.6, §4.7/§4.8).
/// </summary>
public partial class Category : IEntity, IEntityDeletable
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Owning user (FK -> <c>users.id</c>, cascade delete).</summary>
    public ulong UserId { get; set; }

    /// <summary>Display name, 1-100 chars. Unique among the user's active categories (case/accent-insensitive).</summary>
    public required string Name { get; set; }

    /// <summary>Chart color as a <c>#RRGGBB</c> hex string (max length 7).</summary>
    public required string Color { get; set; }

    /// <summary>Optional client-mapped icon key (max 50). The server does not enumerate icons.</summary>
    public string? Icon { get; set; }

    /// <summary>True for the single default category ("danh mục mặc định"): reassigned atomically, not deletable.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Soft-delete flag; deleted categories are excluded by default by <c>BaseRepository.Query</c>.</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
