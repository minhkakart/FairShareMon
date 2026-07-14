using FairShareMonApi.Auth;
using FairShareMonApi.Auth.Abstractions;
using FairShareMonApi.Constants;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for the Redis + DB composite whitelist (OQ7 decision). The Redis-down tests
/// use the unreachable multiplexer and skip only for the DB; they prove the DB is the source of
/// truth and every Redis failure is warn-and-continue. The live-Redis tests additionally skip when
/// Redis is unreachable and prove cache-first reads plus DB-fallback backfill self-healing.
/// </summary>
[Collection("AuthIntegration")]
public class TokenWhitelistStoreTests(DatabaseFixture fixture, RedisFixture redisFixture)
    : AuthDbTestBase(fixture), IClassFixture<DatabaseFixture>, IClassFixture<RedisFixture>
{
    protected override IConnectionMultiplexer? RedisForCleanup =>
        redisFixture.IsAvailable ? redisFixture.Redis : null;

    private TokenWhitelistStore CreateStore(IConnectionMultiplexer redis) =>
        new(new AuthTokenRepository(CreateContext()), redis, NullLogger<TokenWhitelistStore>.Instance);

    private TokenWhitelistStore CreateStoreWithRedisDown() => CreateStore(UnreachableRedis.Instance);

    private static string NewHash() => TokenHasher.Sha256Hex(Uuid.NewV7());

    private static TokenWhitelistEntry NewEntry(string userUuid, string username, DateTime? expiresAt = null) =>
        new(userUuid, expiresAt ?? DateTime.UtcNow.AddMinutes(30), username, TokenTypes.Access, Uuid.NewV7());

    [SkippableFact]
    public async Task AddThenLookup_RedisDown_FallsBackToDbWithoutThrowing()
    {
        var user = await SeedUserAsync();
        var store = CreateStoreWithRedisDown();
        var tokenHash = NewHash();

        await store.AddAsync(tokenHash, NewEntry(user.Uuid, user.Username)); // cache write fails silently (warn-and-continue)
        var entry = await store.LookupAsync(tokenHash); // cache read fails silently -> DB fallback

        Assert.NotNull(entry);
        Assert.Equal(user.Uuid, entry!.UserId);
        Assert.Equal(user.Username, entry.Username);
        Assert.Equal(TokenTypes.Access, entry.TokenType);
    }

    [SkippableFact]
    public async Task LookupAsync_RevokedRow_ReturnsNull()
    {
        var user = await SeedUserAsync();
        var store = CreateStoreWithRedisDown();
        var tokenHash = NewHash();
        await store.AddAsync(tokenHash, NewEntry(user.Uuid, user.Username));

        await store.RemoveAsync(tokenHash); // soft-revokes the DB row
        var entry = await store.LookupAsync(tokenHash);

        Assert.Null(entry); // "in the whitelist" means valid - revoked rows never come back
    }

    [SkippableFact]
    public async Task LookupAsync_ExpiredRow_ReturnsNull()
    {
        var user = await SeedUserAsync();
        var store = CreateStoreWithRedisDown();
        var tokenHash = NewHash();
        await store.AddAsync(tokenHash, NewEntry(user.Uuid, user.Username, DateTime.UtcNow.AddMinutes(-1)));

        var entry = await store.LookupAsync(tokenHash);

        Assert.Null(entry);
    }

    [SkippableFact]
    public async Task AddAsync_UnknownUser_Throws()
    {
        var store = CreateStoreWithRedisDown();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.AddAsync(NewHash(), NewEntry("00000000-0000-7000-8000-000000000000", "ghost")));
    }

    [SkippableFact]
    public async Task LookupAsync_DbFallbackHit_BackfillsRedisWithRemainingTtl()
    {
        redisFixture.SkipIfNoRedis();
        var user = await SeedUserAsync();
        var tokenHash = NewHash();
        await CreateStoreWithRedisDown().AddAsync(tokenHash, NewEntry(user.Uuid, user.Username)); // DB row only, no cache

        var entry = await CreateStore(redisFixture.Redis).LookupAsync(tokenHash); // fallback hit must self-heal the cache

        Assert.NotNull(entry);
        var redisDb = redisFixture.Redis.GetDatabase();
        var cacheKey = TokenWhitelistStore.CacheKey(tokenHash);
        Assert.True(await redisDb.KeyExistsAsync(cacheKey));
        var timeToLive = await redisDb.KeyTimeToLiveAsync(cacheKey);
        Assert.NotNull(timeToLive);
        Assert.InRange(timeToLive!.Value, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30)); // remaining lifetime, not a fixed TTL
    }

    [SkippableFact]
    public async Task LookupAsync_CachedEntry_IsServedCacheFirstWithoutTheDb()
    {
        redisFixture.SkipIfNoRedis();
        var user = await SeedUserAsync();
        var store = CreateStore(redisFixture.Redis);
        var tokenHash = NewHash();
        await store.AddAsync(tokenHash, NewEntry(user.Uuid, user.Username)); // DB row + cache entry

        // Remove the DB row entirely: only the cache can answer now.
        await using (var context = CreateContext())
        {
            await context.AuthTokens.Where(token => token.TokenHash == tokenHash).ExecuteDeleteAsync();
        }

        var entry = await store.LookupAsync(tokenHash);

        Assert.NotNull(entry); // cache-first: no DB hit needed on the hot path
        Assert.Equal(user.Username, entry!.Username);

        await redisFixture.Redis.GetDatabase().KeyDeleteAsync(TokenWhitelistStore.CacheKey(tokenHash)); // tidy the orphaned key
    }
}
