using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Share;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <see cref="EventShareLinkCache"/> over the real MariaDB (+ Redis where a live
/// server is needed), mirroring <c>TokenWhitelistStoreTests</c> and the OQ shared-cache contract: the
/// <c>event_share_links</c> table is the source of truth and Redis is a best-effort cache. The
/// Redis-down tests use the unreachable multiplexer and skip only for the DB - they prove DB fallback
/// and that every Redis failure is warn-and-continue. The live-Redis tests additionally skip when Redis
/// is unreachable and prove cache-first reads, DB-fallback backfill (TTL = remaining lifetime), and
/// delete-on-revoke. Revoked / expired / unknown tokens always resolve to null and are never cached.
/// </summary>
[Collection("AuthIntegration")]
public class EventShareLinkCacheTests(DatabaseFixture fixture, RedisFixture redisFixture)
    : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>, IClassFixture<RedisFixture>
{
    private static readonly DateTime Day14 = new(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day16 = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private readonly List<string> _createdKeys = [];

    private EventShareLinkCache CreateCache(IConnectionMultiplexer redis) =>
        new(new EventShareLinkRepository(CreateContext()), redis, NullLogger<EventShareLinkCache>.Instance);

    private EventShareLinkCache CreateCacheWithRedisDown() => CreateCache(UnreachableRedis.Instance);

    private static string NewToken() => "shr_" + Guid.NewGuid().ToString("N");

    /// <summary>Seeds a user + closed event and inserts a share-link row through the real repository.</summary>
    private async Task<(User User, Event Evt, string Token)> SeedLinkAsync(
        DateTime? expiresAt = null,
        string? bankBin = "970436")
    {
        var user = await SeedUserAsync();
        var evt = await SeedEventAsync(user.Id, "Đà Lạt", Day14, Day16, closed: true);
        var token = NewToken();
        await new EventShareLinkRepository(CreateContext()).CreateAsync(
            user.Uuid, evt.Uuid, token, expiresAt ?? AppDateTime.Now.AddHours(24),
            bankBin is null ? null : "acc", bankBin,
            bankBin is null ? null : "Vietcombank",
            bankBin is null ? null : "0123456789",
            bankBin is null ? null : "Nguyen Van A");
        _createdKeys.Add(EventShareLinkCache.CacheKey(token));
        return (user, evt, token);
    }

    [SkippableFact]
    public async Task LookupAsync_RedisDown_FallsBackToDbAndCarriesOwnerEventAndSnapshot()
    {
        var (user, evt, token) = await SeedLinkAsync();

        var entry = await CreateCacheWithRedisDown().LookupAsync(token); // cache read fails silently -> DB fallback

        Assert.NotNull(entry);
        Assert.Equal(user.Uuid, entry!.OwnerUserUuid);
        Assert.Equal(evt.Uuid, entry.EventUuid);
        Assert.Equal("970436", entry.BankBin);
        Assert.Equal("Vietcombank", entry.BankName);
        Assert.Equal("0123456789", entry.AccountNumber);
    }

    [SkippableFact]
    public async Task LookupAsync_NoSnapshotRow_ResolvesWithNullBankFields()
    {
        var (_, _, token) = await SeedLinkAsync(bankBin: null);

        var entry = await CreateCacheWithRedisDown().LookupAsync(token);

        Assert.NotNull(entry);
        Assert.Null(entry!.BankBin); // hasQr = false at the service layer
    }

    [SkippableFact]
    public async Task LookupAsync_UnknownToken_ReturnsNull()
    {
        Fixture.SkipIfNoDb();

        var entry = await CreateCacheWithRedisDown().LookupAsync(NewToken());

        Assert.Null(entry);
    }

    [SkippableFact]
    public async Task LookupAsync_RevokedRow_ReturnsNull()
    {
        var (user, evt, token) = await SeedLinkAsync();
        await new EventShareLinkRepository(CreateContext()).RevokeActiveByEventAsync(user.Uuid, evt.Uuid);

        var entry = await CreateCacheWithRedisDown().LookupAsync(token);

        Assert.Null(entry); // soft-revoked rows never resolve
    }

    [SkippableFact]
    public async Task LookupAsync_ExpiredRow_ReturnsNull()
    {
        var (_, _, token) = await SeedLinkAsync(expiresAt: AppDateTime.Now.AddHours(-1));

        var entry = await CreateCacheWithRedisDown().LookupAsync(token);

        Assert.Null(entry);
    }

    [SkippableFact]
    public async Task AddAsync_RedisDown_DoesNotThrow()
    {
        var (user, evt, token) = await SeedLinkAsync();
        var entry = new EventShareLinkEntry(user.Uuid, evt.Uuid, AppDateTime.Now.AddHours(24), "acc", "970436", "Vietcombank", "0123456789", "Nguyen Van A");

        await CreateCacheWithRedisDown().AddAsync(token, entry); // warn-and-continue; no throw
    }

    [SkippableFact]
    public async Task LookupAsync_DbFallbackHit_BackfillsRedisWithRemainingTtl()
    {
        redisFixture.SkipIfNoRedis();
        var (_, _, token) = await SeedLinkAsync(); // DB row only (no cache write yet)

        var entry = await CreateCache(redisFixture.Redis).LookupAsync(token); // fallback hit must self-heal the cache

        Assert.NotNull(entry);
        var redisDb = redisFixture.Redis.GetDatabase();
        var cacheKey = EventShareLinkCache.CacheKey(token);
        Assert.True(await redisDb.KeyExistsAsync(cacheKey));
        var timeToLive = await redisDb.KeyTimeToLiveAsync(cacheKey);
        Assert.NotNull(timeToLive);
        Assert.InRange(timeToLive!.Value, TimeSpan.FromHours(23), TimeSpan.FromHours(24)); // remaining lifetime, not a fixed TTL
    }

    [SkippableFact]
    public async Task LookupAsync_CachedEntry_IsServedCacheFirstWithoutTheDb()
    {
        redisFixture.SkipIfNoRedis();
        var (user, evt, token) = await SeedLinkAsync();
        var cache = CreateCache(redisFixture.Redis);
        await cache.AddAsync(token, new EventShareLinkEntry(user.Uuid, evt.Uuid, AppDateTime.Now.AddHours(24), "acc", "970436", "Vietcombank", "0123456789", "Nguyen Van A"));

        // Remove the DB row entirely: only the cache can answer now.
        await using (var context = CreateContext())
            await context.EventShareLinks.Where(link => link.Token == token).ExecuteDeleteAsync();

        var entry = await cache.LookupAsync(token);

        Assert.NotNull(entry); // cache-first: no DB row needed on the hot path
        Assert.Equal(user.Uuid, entry!.OwnerUserUuid);
    }

    [SkippableFact]
    public async Task RemoveAsync_EvictsCachedKey()
    {
        redisFixture.SkipIfNoRedis();
        var (user, evt, token) = await SeedLinkAsync();
        var cache = CreateCache(redisFixture.Redis);
        await cache.AddAsync(token, new EventShareLinkEntry(user.Uuid, evt.Uuid, AppDateTime.Now.AddHours(24), "acc", "970436", "Vietcombank", "0123456789", "Nguyen Van A"));
        Assert.True(await redisFixture.Redis.GetDatabase().KeyExistsAsync(EventShareLinkCache.CacheKey(token)));

        await cache.RemoveAsync(token);

        Assert.False(await redisFixture.Redis.GetDatabase().KeyExistsAsync(EventShareLinkCache.CacheKey(token)));
    }

    protected override IConnectionMultiplexer? RedisForCleanup =>
        redisFixture.IsAvailable ? redisFixture.Redis : null;

    public override async Task DisposeAsync()
    {
        if (redisFixture.IsAvailable)
        {
            foreach (var key in _createdKeys)
            {
                try { await redisFixture.Redis.GetDatabase().KeyDeleteAsync(key); }
                catch { /* best-effort - orphaned keys expire with their TTL */ }
            }
        }

        // The event_share_links rows cascade-delete with their user/event in the base cleanup.
        await base.DisposeAsync();
    }
}
