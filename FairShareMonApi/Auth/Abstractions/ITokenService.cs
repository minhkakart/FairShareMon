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
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a new access + refresh pair for the user. The username is whitelisted alongside the
    /// hash so per-request validation needs no DB hit. Null when issuance fails (unknown user).
    /// </summary>
    Task<TokenPair?> IssueAsync(string userId, string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a valid refresh token for a new pair (full pair rotation - the old refresh AND
    /// its paired access token are revoked). Null when the refresh token is invalid; presenting a
    /// REVOKED refresh token is treated as theft and revokes ALL of the user's sessions.
    /// </summary>
    Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes the presented raw token's whole pair (logout). False when the token was not whitelisted.</summary>
    Task<bool> RevokeAsync(string rawToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes every token of the user (e.g. on password change). Returns the number of revoked tokens.</summary>
    Task<int> RevokeAllAsync(string userId, CancellationToken cancellationToken = default);
}
