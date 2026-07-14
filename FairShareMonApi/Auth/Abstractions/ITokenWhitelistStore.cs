namespace FairShareMonApi.Auth.Abstractions;

/// <summary>
/// A whitelisted token entry, keyed by SHA-256(raw token). Carries the username so per-request
/// validation can materialize <see cref="AuthenticatedUser"/> without a DB hit, the token type so
/// refresh tokens can never authenticate as access tokens, and the pair uuid linking the two rows
/// of one issuance (logout/rotation revoke the whole pair).
/// </summary>
public record TokenWhitelistEntry(
    string UserId,
    DateTime ExpiresAt,
    string Username,
    string TokenType,
    string PairUuid);

/// <summary>
/// Hash-keyed token whitelist - a composite of Redis (cache, TTL = expiry) and the
/// <c>auth_tokens</c> table (source of truth). Lookups are cache-first with DB fallback and
/// exclude revoked rows; every Redis operation is best-effort (warn-and-continue on outage).
/// Only token hashes ever cross this boundary - never raw tokens.
/// </summary>
public interface ITokenWhitelistStore
{
    Task AddAsync(string tokenHash, TokenWhitelistEntry entry, CancellationToken cancellationToken = default);

    Task<TokenWhitelistEntry?> LookupAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task RemoveAsync(string tokenHash, CancellationToken cancellationToken = default);
}
