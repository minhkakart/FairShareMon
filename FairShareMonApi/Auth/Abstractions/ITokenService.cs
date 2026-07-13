namespace FairShareMonApi.Auth.Abstractions;

/// <summary>Access + refresh token pair, returned to the client exactly once (raw tokens are never stored).</summary>
public record TokenPair(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);

/// <summary>
/// Opaque stateful token lifecycle (see CLAUDE.md - Auth): issuing generates random tokens, stores
/// their SHA-256 hashes in the whitelist (DB + Redis, TTL = expiry) and returns the raw pair once.
/// No real implementation exists yet - the auth feature provides it (and must DELETE the stub).
/// </summary>
public interface ITokenService
{
    /// <summary>Issues a new access + refresh pair for the user. Null when issuance fails.</summary>
    Task<TokenPair?> IssueAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Exchanges a valid refresh token for a new pair, revoking the old one. Null when the refresh token is invalid.</summary>
    Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes a single raw token (logout). False when the token was not whitelisted.</summary>
    Task<bool> RevokeAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes every token of the user (e.g. on password change). Returns the number of revoked tokens.</summary>
    Task<int> RevokeAllAsync(string userId, CancellationToken cancellationToken = default);
}
