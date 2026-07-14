using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Xunit;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Base for auth integration tests that exercise repositories/services against the real MariaDB.
///
/// DELIBERATE DEVIATION from <see cref="IntegrationTestBase"/>'s rollback harness: repository and
/// service writes go through <c>ExecuteTransactionAsync</c>, which begins its OWN database
/// transaction - that cannot nest inside the harness's already-open per-test transaction (EF Core
/// throws). Isolation is achieved instead by a unique lowercase username prefix per test class and
/// guaranteed cleanup on dispose: deleting the prefix's users cascades to their
/// <c>auth_tokens</c> rows, and the users' cached token keys are best-effort removed from Redis
/// first, so repeated runs stay deterministic and the real DB/Redis are never left dirty.
/// </summary>
public abstract class AuthDbTestBase(DatabaseFixture fixture) : IAsyncLifetime
{
    private DbContextOptions<AppDbContext>? _contextOptions;
    private int _usernameCounter;

    protected DatabaseFixture Fixture { get; } = fixture;

    /// <summary>Unique per test-class instance; valid username charset (lowercase hex + '_').</summary>
    protected string UsernamePrefix { get; } = "t" + Guid.NewGuid().ToString("N")[..10] + "_";

    /// <summary>Multiplexer used to clean cached token keys on dispose; null = skip Redis cleanup.</summary>
    protected virtual IConnectionMultiplexer? RedisForCleanup => null;

    public virtual Task InitializeAsync()
    {
        Fixture.SkipIfNoDb();

        _contextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(Fixture.ConnectionString, new MariaDbServerVersion(new Version(11, 7, 2)))
            .Options;
        return Task.CompletedTask;
    }

    /// <summary>A context on its own connection - repositories manage their own transactions on it.</summary>
    protected AppDbContext CreateContext() =>
        new(_contextOptions ?? throw new InvalidOperationException("InitializeAsync has not run."));

    protected string NewUsername() => UsernamePrefix + Interlocked.Increment(ref _usernameCounter);

    /// <summary>Seeds a user row directly (no repository) and returns it with its DB-assigned Id.</summary>
    protected async Task<User> SeedUserAsync(string? username = null, string passwordHash = "seeded-hash-not-a-password")
    {
        await using var context = CreateContext();
        var user = new User { Username = username ?? NewUsername(), PasswordHash = passwordHash };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public virtual async Task DisposeAsync()
    {
        if (_contextOptions is null)
            return; // test skipped before setup - nothing to clean

        await using var context = CreateContext();

        var redis = RedisForCleanup;
        if (redis is not null)
        {
            var tokenHashes = await context.AuthTokens
                .Where(token => token.User.Username.StartsWith(UsernamePrefix))
                .Select(token => token.TokenHash)
                .ToListAsync();

            foreach (var tokenHash in tokenHashes)
            {
                try
                {
                    await redis.GetDatabase().KeyDeleteAsync("auth:token:" + tokenHash);
                }
                catch
                {
                    // Best-effort - orphaned keys expire with their TTL.
                }
            }
        }

        // Cascade delete wipes the users' auth_tokens rows too.
        await context.Users.Where(user => user.Username.StartsWith(UsernamePrefix)).ExecuteDeleteAsync();
    }
}
