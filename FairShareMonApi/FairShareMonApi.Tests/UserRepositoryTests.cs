using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>UserRepository</c> against the real MariaDB (skippable). Covers the
/// OQ2 storage behavior (lowercase stored, case-insensitive retrieval via utf8mb4_unicode_ci),
/// entity defaults (uuid, FREE tier, UTC timestamps), and the duplicate-username guard.
/// </summary>
[Collection("AuthIntegration")]
public class UserRepositoryTests(DatabaseFixture fixture) : AuthDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private UserRepository CreateRepository() => new(CreateContext());

    private static User NewUser(string username) => new() { Username = username, PasswordHash = "hash-placeholder" };

    [SkippableFact]
    public async Task CreateAsync_NewUser_PersistsWithUuidFreeTierAndUtcTimestamps()
    {
        var username = NewUsername();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var created = await CreateRepository().CreateAsync(NewUser(username));
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.NotNull(created);
        Assert.Equal(36, created!.Uuid.Length);
        Assert.Equal(UserTiers.Free, created.Tier); // FREE is the registration default (§3.11)
        Assert.InRange(created.CreatedAt, before, after); // AppDateTime.Now = UTC (OQ10)

        await using var context = CreateContext();
        var persisted = await context.Users.AsNoTracking().SingleAsync(user => user.Username == username);
        Assert.Equal(created.Uuid, persisted.Uuid);
        Assert.Equal(UserTiers.Free, persisted.Tier);
    }

    [SkippableFact]
    public async Task CreateAsync_DuplicateUsername_ReturnsNullAndPersistsNothing()
    {
        var username = NewUsername();
        var repository = CreateRepository();
        await repository.CreateAsync(NewUser(username));

        var duplicate = await CreateRepository().CreateAsync(NewUser(username));

        Assert.Null(duplicate);
        await using var context = CreateContext();
        Assert.Equal(1, await context.Users.CountAsync(user => user.Username == username));
    }

    [SkippableFact]
    public async Task GetByUsernameAsync_DifferentCasing_FindsLowercaseStoredUser()
    {
        var username = NewUsername();
        await SeedUserAsync(username);

        // utf8mb4_unicode_ci makes the lookup case-insensitive at the DB level (OQ2 behavior).
        var found = await CreateRepository().GetByUsernameAsync(username.ToUpperInvariant());

        Assert.NotNull(found);
        Assert.Equal(username, found!.Username);
    }

    [SkippableFact]
    public async Task GetByUuidAsync_KnownAndUnknown_ReturnsUserOrNull()
    {
        var seeded = await SeedUserAsync();
        var repository = CreateRepository();

        var found = await repository.GetByUuidAsync(seeded.Uuid);
        var missing = await repository.GetByUuidAsync("00000000-0000-7000-8000-000000000000");

        Assert.NotNull(found);
        Assert.Equal(seeded.Username, found!.Username);
        Assert.Null(missing);
    }

    [SkippableFact]
    public async Task ExistsByUsernameAsync_ExistingAndMissing_ReturnsExpected()
    {
        var username = NewUsername();
        await SeedUserAsync(username);
        var repository = CreateRepository();

        Assert.True(await repository.ExistsByUsernameAsync(username));
        Assert.False(await repository.ExistsByUsernameAsync(UsernamePrefix + "missing"));
    }

    [SkippableFact]
    public async Task UpdatePasswordAsync_ExistingUser_ReplacesHash()
    {
        var seeded = await SeedUserAsync(passwordHash: "old-hash");

        var updated = await CreateRepository().UpdatePasswordAsync(seeded.Uuid, "new-hash");

        Assert.True(updated);
        await using var context = CreateContext();
        var persisted = await context.Users.AsNoTracking().SingleAsync(user => user.Uuid == seeded.Uuid);
        Assert.Equal("new-hash", persisted.PasswordHash);
    }

    [SkippableFact]
    public async Task UpdatePasswordAsync_UnknownUuid_ReturnsFalse()
    {
        var updated = await CreateRepository().UpdatePasswordAsync("00000000-0000-7000-8000-000000000000", "new-hash");

        Assert.False(updated);
    }
}
