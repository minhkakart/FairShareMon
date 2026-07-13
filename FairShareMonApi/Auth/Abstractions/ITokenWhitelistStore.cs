namespace FairShareMonApi.Auth.Abstractions;

/// <summary>A whitelisted token entry, keyed by SHA-256(raw token).</summary>
public record TokenWhitelistEntry(string UserId, DateTime ExpiresAt);

/// <summary>
/// Hash-keyed token whitelist. The future implementation is a composite: Redis first (TTL =
/// expiry), the auth_tokens table as fallback. Only token hashes ever cross this boundary -
/// never raw tokens.
/// </summary>
public interface ITokenWhitelistStore
{
    Task AddAsync(string tokenHash, TokenWhitelistEntry entry, CancellationToken cancellationToken = default);

    Task<TokenWhitelistEntry?> LookupAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task RemoveAsync(string tokenHash, CancellationToken cancellationToken = default);
}
