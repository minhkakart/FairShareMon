using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// Whitelisted opaque token (table <c>auth_tokens</c>), keyed by SHA-256(raw token) - raw tokens
/// are never persisted. The two rows of one issuance (ACCESS + REFRESH) share a
/// <see cref="PairUuid"/> so logout/rotation can revoke the whole pair. Rotation/logout set
/// <see cref="RevokedAt"/> (soft-revoke, kept until natural expiry) so a reused revoked refresh
/// token stays attributable for the reuse-detection cascade; password-change revoke-all
/// hard-deletes instead.
/// </summary>
public partial class AuthToken : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    public ulong UserId { get; set; }

    /// <summary>Lowercase-hex SHA-256 of the raw token; fixed 64 chars, unique.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Token type per <see cref="Constants.TokenTypes"/> (ACCESS / REFRESH).</summary>
    public required string TokenType { get; set; }

    /// <summary>Shared by the two rows of one issuance; enables pair/rotation revocation.</summary>
    public required string PairUuid { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Null = active. Set by rotation/logout (soft-revoke); row purged after expiry.</summary>
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
