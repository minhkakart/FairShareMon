using System.Text.Json;
using DiDecoration.Attributes;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Repositories;
using FairShareMonApi.Utils;
using StackExchange.Redis;

namespace FairShareMonApi.Auth;

/// <summary>
/// Redis + DB composite whitelist (OQ7 decision, planning/user-authentication.md): the
/// <c>auth_tokens</c> table is the source of truth, Redis is a best-effort cache - lookups fall
/// back to the DB (excluding revoked rows) and backfill Redis with the remaining TTL; cache
/// writes/deletes warn-and-continue on failure. The bounded stale-revocation window (max one
/// access-token TTL when a revocation's Redis delete failed) is explicitly accepted.
/// </summary>
[ScopedService(typeof(ITokenWhitelistStore))]
public sealed class TokenWhitelistStore(
    IAuthTokenRepository authTokenRepository,
    IConnectionMultiplexer redis,
    ILogger<TokenWhitelistStore> logger) : ITokenWhitelistStore
{
    private const string KeyPrefix = "auth:token:";

    public static string CacheKey(string tokenHash) => KeyPrefix + tokenHash;

    public static string Serialize(TokenWhitelistEntry entry) => JsonSerializer.Serialize(entry);

    public async Task AddAsync(string tokenHash, TokenWhitelistEntry entry, CancellationToken cancellationToken = default)
    {
        var added = await authTokenRepository.AddAsync(
            entry.UserId, tokenHash, entry.TokenType, entry.PairUuid, entry.ExpiresAt, cancellationToken);
        if (!added)
            throw new InvalidOperationException($"Cannot whitelist a token for unknown user '{entry.UserId}'.");

        await TryCacheAsync(redis, logger, tokenHash, entry);
    }

    public async Task<TokenWhitelistEntry?> LookupAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var cached = await TryGetCachedAsync(tokenHash);
        if (cached is not null)
            return cached;

        var row = await authTokenRepository.GetByHashWithUserAsync(tokenHash, cancellationToken);
        if (row is null || row.RevokedAt is not null || row.ExpiresAt <= AppDateTime.Now)
            return null;

        var entry = new TokenWhitelistEntry(row.UserUuid, row.ExpiresAt, row.Username, row.TokenType, row.PairUuid, row.Tier);
        await TryCacheAsync(redis, logger, tokenHash, entry); // self-heal: backfill the cache on a DB-fallback hit

        // Guard the backfill race: a revocation committing between the read above and the cache
        // write leaves a stale-valid entry (a revoker always deletes the cache AFTER the DB update,
        // so its delete could precede our write). Re-read; if the row became revoked/gone in that
        // window, evict what we just wrote and deny - so a stale entry can't outlive its DB row.
        var recheck = await authTokenRepository.GetByHashWithUserAsync(tokenHash, cancellationToken);
        if (recheck is null || recheck.RevokedAt is not null || recheck.ExpiresAt <= AppDateTime.Now)
        {
            await TryDeleteCachedAsync(redis, logger, tokenHash);
            return null;
        }

        return entry;
    }

    public async Task RemoveAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        await authTokenRepository.RevokeByHashAsync(tokenHash, cancellationToken);
        await TryDeleteCachedAsync(redis, logger, tokenHash);
    }

    /// <summary>Best-effort Redis write with TTL = remaining lifetime; warns and continues on failure.</summary>
    internal static async Task TryCacheAsync(IConnectionMultiplexer redis, ILogger logger, string tokenHash, TokenWhitelistEntry entry)
    {
        var timeToLive = entry.ExpiresAt - AppDateTime.Now;
        if (timeToLive <= TimeSpan.Zero)
            return;

        try
        {
            await redis.GetDatabase().StringSetAsync(CacheKey(tokenHash), Serialize(entry), timeToLive);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis token-cache write failed; continuing with DB only.");
        }
    }

    /// <summary>Best-effort Redis delete; warns and continues on failure (bounded stale window accepted).</summary>
    internal static async Task TryDeleteCachedAsync(IConnectionMultiplexer redis, ILogger logger, string tokenHash)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(CacheKey(tokenHash));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis token-cache delete failed; entry expires with its TTL.");
        }
    }

    private async Task<TokenWhitelistEntry?> TryGetCachedAsync(string tokenHash)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(CacheKey(tokenHash));
            if (value.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<TokenWhitelistEntry>(value.ToString());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis token-cache read failed; falling back to the database.");
            return null;
        }
    }
}
