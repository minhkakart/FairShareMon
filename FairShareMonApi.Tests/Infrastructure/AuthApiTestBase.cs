using FairShareMonApi.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Base for auth tests that go through the application itself (WebApplicationFactory - either
/// resolving services from its DI container or issuing real HTTP calls). These paths hit the REAL
/// MariaDB/Redis with no rollback envelope, so isolation comes from a unique username prefix per
/// test class and guaranteed cleanup on dispose: cached token keys are best-effort deleted from
/// Redis (resolved from the app's own multiplexer), then the prefix's users are deleted (cascade
/// removes their auth_tokens rows). Tests skip when MariaDB is unreachable.
/// </summary>
public abstract class AuthApiTestBase(WebApplicationFactory<Program> factory, DatabaseFixture fixture) : IAsyncLifetime
{
    private int _usernameCounter;

    protected WebApplicationFactory<Program> Factory { get; } = factory;

    protected DatabaseFixture Fixture { get; } = fixture;

    /// <summary>Unique per test-class instance; valid username charset (lowercase hex + '_').</summary>
    protected string UsernamePrefix { get; } = "t" + Guid.NewGuid().ToString("N")[..10] + "_";

    public virtual Task InitializeAsync()
    {
        Fixture.SkipIfNoDb();
        return Task.CompletedTask;
    }

    protected string NewUsername() => UsernamePrefix + Interlocked.Increment(ref _usernameCounter);

    protected IServiceScope CreateScope() => Factory.Services.CreateScope();

    public virtual async Task DisposeAsync()
    {
        if (!Fixture.IsAvailable)
            return; // tests skipped - nothing was created

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tokenHashes = await context.AuthTokens
            .Where(token => token.User.Username.StartsWith(UsernamePrefix))
            .Select(token => token.TokenHash)
            .ToListAsync();

        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
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

        await context.Users.Where(user => user.Username.StartsWith(UsernamePrefix)).ExecuteDeleteAsync();
    }
}
