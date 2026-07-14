using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Account owner (table <c>users</c>). Username is stored lowercase and unique case-insensitively
/// (utf8mb4_unicode_ci); the password is a BCrypt hash - never plaintext. Users are NOT
/// soft-deletable (confirmed 2026-07-13, planning/user-authentication.md).
/// </summary>
public partial class User : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Lowercase login name, 3-32 chars of <c>a-z 0-9 _ . -</c>, unique.</summary>
    public required string Username { get; set; }

    /// <summary>BCrypt hash (60 chars) of the password.</summary>
    public required string PasswordHash { get; set; }

    /// <summary>Tier per <see cref="Constants.UserTiers"/>; FREE by default on registration.</summary>
    public string Tier { get; set; }

    /// <summary>Role per <see cref="Constants.UserRoles"/>; USER by default (M11). ADMIN unlocks admin management.</summary>
    public string Role { get; set; }

    /// <summary>Account status per <see cref="Constants.UserStatuses"/>; ACTIVE by default (M11). DISABLED blocks login.</summary>
    public string Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
