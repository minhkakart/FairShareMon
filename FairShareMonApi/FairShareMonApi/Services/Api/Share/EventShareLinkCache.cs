using System.Text.Json;
using DiDecoration.Attributes;
using FairShareMonApi.Repositories;
using FairShareMonApi.Utils;
using StackExchange.Redis;

namespace FairShareMonApi.Services.Api.Share;

/// <summary>
/// Cached, token-keyed resolution of a share link's metadata (planning/event-share-link.md, Step 3),
/// mirroring <c>Auth/TokenWhitelistStore</c>: the <c>event_share_links</c> table is the source of
/// truth and Redis is a best-effort cache keyed <c>share:event:{token}</c> with TTL = remaining
/// lifetime. Lookups are cache-first with DB fallback and self-heal backfill; revoked/expired/unknown
/// rows resolve to null (and are never cached); every Redis operation warns-and-continues on failure.
/// Only link <b>metadata</b> is cached (owner UUID, event UUID, expiry, bank snapshot) - never the
/// report payload, which is always recomputed live.
/// </summary>
public interface IEventShareLinkCache
{
    /// <summary>Cache-first resolution with DB fallback + backfill. Null when the token is unknown, revoked, or expired.</summary>
    Task<EventShareLinkEntry?> LookupAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Best-effort cache write with TTL = remaining lifetime (the DB row is written by the create transaction first).</summary>
    Task AddAsync(string token, EventShareLinkEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Best-effort cache delete (called after a revoke commits).</summary>
    Task RemoveAsync(string token, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IEventShareLinkCache"/>
[ScopedService(typeof(IEventShareLinkCache))]
public sealed class EventShareLinkCache(
    IEventShareLinkRepository shareLinkRepository,
    IConnectionMultiplexer redis,
    ILogger<EventShareLinkCache> logger) : IEventShareLinkCache
{
    private const string KeyPrefix = "share:event:";

    public static string CacheKey(string token) => KeyPrefix + token;

    /// <summary>Cache-first resolution with DB fallback + backfill. Null when the token is unknown, revoked, or expired.</summary>
    public async Task<EventShareLinkEntry?> LookupAsync(string token, CancellationToken cancellationToken = default)
    {
        var cached = await TryGetCachedAsync(token);
        if (cached is not null)
            return cached;

        var row = await shareLinkRepository.GetByTokenAsync(token, cancellationToken);
        if (row is null || row.RevokedAt is not null || row.ExpiresAt <= AppDateTime.Now)
            return null;

        var entry = new EventShareLinkEntry(
            row.User.Uuid,
            row.Event.Uuid,
            row.ExpiresAt,
            row.BankAccountUuid,
            row.BankBin,
            row.BankName,
            row.AccountNumber,
            row.AccountHolderName);

        await TryCacheAsync(token, entry); // self-heal: backfill the cache on a DB-fallback hit
        return entry;
    }

    /// <summary>Best-effort cache write with TTL = remaining lifetime (the DB row is written by the create transaction first).</summary>
    public Task AddAsync(string token, EventShareLinkEntry entry, CancellationToken cancellationToken = default) =>
        TryCacheAsync(token, entry);

    /// <summary>Best-effort cache delete (called after a revoke commits).</summary>
    public async Task RemoveAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(CacheKey(token));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis share-link cache delete failed; entry expires with its TTL.");
        }
    }

    private async Task TryCacheAsync(string token, EventShareLinkEntry entry)
    {
        var timeToLive = entry.ExpiresAt - AppDateTime.Now;
        if (timeToLive <= TimeSpan.Zero)
            return;

        try
        {
            await redis.GetDatabase().StringSetAsync(CacheKey(token), JsonSerializer.Serialize(entry), timeToLive);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis share-link cache write failed; continuing with DB only.");
        }
    }

    private async Task<EventShareLinkEntry?> TryGetCachedAsync(string token)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(CacheKey(token));
            if (value.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<EventShareLinkEntry>(value.ToString());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Redis share-link cache read failed; falling back to the database.");
            return null;
        }
    }
}

/// <summary>
/// Cached metadata of a share link, resolved from a public token. Carries the owner + event UUIDs to
/// drive the LIVE read (report is recomputed on each request), the expiry, and the optional bank
/// snapshot (all null when the link has no snapshot, OQ4b - <see cref="BankBin"/> null =&gt; hasQr false).
/// </summary>
public record EventShareLinkEntry(
    string OwnerUserUuid,
    string EventUuid,
    DateTime ExpiresAt,
    string? BankAccountUuid,
    string? BankBin,
    string? BankName,
    string? AccountNumber,
    string? AccountHolderName);
