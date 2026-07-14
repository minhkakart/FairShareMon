using System.Reflection;
using FairShareMonApi.Auth;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.HostedServices;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests (real MariaDB, skippable) for <see cref="AdminSeederHostedService"/> (M11, OQ9). The
/// seeder's <c>ExecuteAsync</c> is driven directly (via reflection) over a real DI scope. Proves it
/// creates the admin when absent (ADMIN role, Free tier, ACTIVE, a BCrypt hash - never the plaintext),
/// promotes an existing account to ADMIN WITHOUT overwriting its password, no-ops when the config is
/// absent, and is idempotent across two runs. The configured username carries the class prefix so the
/// seeded account is swept on dispose (the real DB is never left dirty).
/// </summary>
[Collection("AuthIntegration")]
public class AdminSeederHostedServiceTests(DatabaseFixture fixture) : AdminDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private const string Password = "seed-password-8+";

    private async Task RunSeederAsync(string? username, string? password)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(Fixture.ConnectionString, new MariaDbServerVersion(new Version(11, 7, 2))));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:BcryptWorkFactor"] = "4" }).Build());
        await using var provider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Admin:Username"] = username,
                ["Admin:Password"] = password
            })
            .Build();

        var seeder = new AdminSeederHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(), config, NullLogger<AdminSeederHostedService>.Instance);

        var executeAsync = typeof(AdminSeederHostedService)
            .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)executeAsync.Invoke(seeder, [CancellationToken.None])!;
    }

    [SkippableFact]
    public async Task Seeder_ConfiguredUsernameAbsent_CreatesAdminAccount()
    {
        Fixture.SkipIfNoDb();
        var username = UsernamePrefix + "admin";

        await RunSeederAsync(username, Password);

        await using var context = CreateContext();
        var created = await context.Users.AsNoTracking().SingleAsync(user => user.Username == username);
        Assert.Equal(UserRoles.Admin, created.Role);
        Assert.Equal(UserTiers.Free, created.Tier);
        Assert.Equal(UserStatuses.Active, created.Status);
        Assert.NotEqual(Password, created.PasswordHash);                  // stored a hash, never the plaintext
        Assert.True(new PasswordHasher(BuildHashConfig()).Verify(Password, created.PasswordHash)); // and it verifies
    }

    [SkippableFact]
    public async Task Seeder_ExistingUser_PromotedToAdmin_WithoutOverwritingPassword()
    {
        Fixture.SkipIfNoDb();
        var existing = await SeedUserAsync(role: UserRoles.User, username: UsernamePrefix + "promote");
        var originalHash = existing.PasswordHash;

        await RunSeederAsync(existing.Username, "a-different-password");

        var reloaded = await ReloadUserAsync(existing.Uuid);
        Assert.Equal(UserRoles.Admin, reloaded!.Role);        // promoted
        Assert.Equal(originalHash, reloaded.PasswordHash);    // password NOT overwritten (OQ9c rejected)
    }

    [SkippableFact]
    public async Task Seeder_ConfigAbsent_NoOp_CreatesNoAccount()
    {
        Fixture.SkipIfNoDb();
        var countBefore = await CountPrefixUsersAsync();

        await RunSeederAsync(username: null, password: null);
        await RunSeederAsync(username: UsernamePrefix + "noop", password: null); // password missing -> also a no-op

        Assert.Equal(countBefore, await CountPrefixUsersAsync());
    }

    [SkippableFact]
    public async Task Seeder_RunTwice_IsIdempotent_SingleAdminNoDuplicate()
    {
        Fixture.SkipIfNoDb();
        var username = UsernamePrefix + "idem";

        await RunSeederAsync(username, Password);
        await RunSeederAsync(username, Password); // second run must not create a duplicate or throw

        await using var context = CreateContext();
        var admins = await context.Users.AsNoTracking().Where(user => user.Username == username).ToListAsync();
        var admin = Assert.Single(admins);
        Assert.Equal(UserRoles.Admin, admin.Role);
    }

    private static IConfiguration BuildHashConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:BcryptWorkFactor"] = "4" }).Build();

    private async Task<int> CountPrefixUsersAsync()
    {
        await using var context = CreateContext();
        return await context.Users.CountAsync(user => user.Username.StartsWith(UsernamePrefix));
    }
}
