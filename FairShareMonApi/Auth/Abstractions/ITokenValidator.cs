namespace FairShareMonApi.Auth.Abstractions;

/// <summary>
/// Validates a raw bearer token for the authentication handler. The future implementation hashes
/// the token (SHA-256) and checks the whitelist - cache first, DB fallback.
/// </summary>
public interface ITokenValidator
{
    /// <summary>Raw bearer token -> authenticated user; null when the token is unknown, expired, or revoked.</summary>
    Task<AuthenticatedUser?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default);
}
