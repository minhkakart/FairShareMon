using FairShareMonApi.Constants;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Admin;
using FairShareMonApi.Tests.Infrastructure;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests (real MariaDB, skippable) for the M11 admin methods on <see cref="UserRepository"/>:
/// <c>ListForAdminAsync</c> (paged/filtered/sorted account-metadata listing - scoped in these tests to
/// the class's unique username prefix so the global listing is deterministic), <c>SetTierAsync</c>/
/// <c>SetStatusAsync</c>/<c>SetRoleAsync</c> (targeted single-field writes; false on an unknown uuid), and
/// <c>CountByRoleAsync</c> (feeds the last-admin guard). The listing projects <see cref="AdminUserAccount"/>
/// which carries account metadata ONLY - no ledger field exists on it (R10, a compile-time guarantee).
/// </summary>
[Collection("AuthIntegration")]
public class AdminUserRepositoryTests(DatabaseFixture fixture) : AdminDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private UserRepository CreateRepository() => new(CreateContext());

    private AdminUserQuery Query(
        string? tier = null, string? status = null, string? role = null,
        int page = 1, int pageSize = 20, string sort = "createdAt", bool descending = true) =>
        new(tier, status, role, UsernamePrefix, page, pageSize, sort, descending);

    // ---- Listing: filters ---------------------------------------------------------------------------

    [SkippableFact]
    public async Task ListForAdminAsync_SearchScopesToPrefix_ReturnsOnlyAccountMetadata()
    {
        Fixture.SkipIfNoDb();
        await SeedUserAsync(tier: UserTiers.Free);
        await SeedUserAsync(tier: UserTiers.Premium);

        var (rows, total) = await CreateRepository().ListForAdminAsync(Query());

        Assert.Equal(2, total);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.StartsWith(UsernamePrefix, row.Username));
        Assert.All(rows, row => Assert.NotEqual(0ul, row.Id));
    }

    [SkippableFact]
    public async Task ListForAdminAsync_FilterByTier_ReturnsOnlyThatTier()
    {
        Fixture.SkipIfNoDb();
        await SeedUserAsync(tier: UserTiers.Free);
        var premium = await SeedUserAsync(tier: UserTiers.Premium);

        var (rows, total) = await CreateRepository().ListForAdminAsync(Query(tier: UserTiers.Premium));

        Assert.Equal(1, total);
        Assert.Equal(premium.Uuid, Assert.Single(rows).Uuid);
    }

    [SkippableFact]
    public async Task ListForAdminAsync_FilterByRole_ReturnsOnlyThatRole()
    {
        Fixture.SkipIfNoDb();
        await SeedUserAsync(role: UserRoles.User);
        var admin = await SeedUserAsync(role: UserRoles.Admin);

        var (rows, _) = await CreateRepository().ListForAdminAsync(Query(role: UserRoles.Admin));

        Assert.Equal(admin.Uuid, Assert.Single(rows).Uuid);
    }

    [SkippableFact]
    public async Task ListForAdminAsync_FilterByStatus_ReturnsOnlyThatStatus()
    {
        Fixture.SkipIfNoDb();
        await SeedUserAsync(status: UserStatuses.Active);
        var disabled = await SeedUserAsync(status: UserStatuses.Disabled);

        var (rows, _) = await CreateRepository().ListForAdminAsync(Query(status: UserStatuses.Disabled));

        Assert.Equal(disabled.Uuid, Assert.Single(rows).Uuid);
    }

    // ---- Listing: paging + sort ---------------------------------------------------------------------

    [SkippableFact]
    public async Task ListForAdminAsync_Paging_SlicesAndReportsTotal()
    {
        Fixture.SkipIfNoDb();
        await SeedUserAsync();
        await SeedUserAsync();
        await SeedUserAsync();

        var repository = CreateRepository();
        var (page1, total1) = await repository.ListForAdminAsync(Query(page: 1, pageSize: 2));
        var (page2, total2) = await repository.ListForAdminAsync(Query(page: 2, pageSize: 2));

        Assert.Equal(3, total1);
        Assert.Equal(3, total2);
        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
    }

    [SkippableFact]
    public async Task ListForAdminAsync_SortByUsername_HonoursDirection()
    {
        Fixture.SkipIfNoDb();
        // NewUsername() appends an increasing counter, so seed order == username ascending order.
        var first = await SeedUserAsync();
        var second = await SeedUserAsync();

        var (asc, _) = await CreateRepository().ListForAdminAsync(Query(sort: "username", descending: false));
        var (desc, _) = await CreateRepository().ListForAdminAsync(Query(sort: "username", descending: true));

        Assert.Equal(first.Username, asc[0].Username);
        Assert.Equal(second.Username, desc[0].Username);
    }

    // ---- Targeted field writes ----------------------------------------------------------------------

    [SkippableFact]
    public async Task SetTierAsync_UpdatesTier_ReturnsTrue()
    {
        Fixture.SkipIfNoDb();
        var user = await SeedUserAsync(tier: UserTiers.Free);

        Assert.True(await CreateRepository().SetTierAsync(user.Uuid, UserTiers.Premium));
        Assert.Equal(UserTiers.Premium, (await ReloadUserAsync(user.Uuid))!.Tier);
    }

    [SkippableFact]
    public async Task SetStatusAsync_UpdatesStatus_ReturnsTrue()
    {
        Fixture.SkipIfNoDb();
        var user = await SeedUserAsync(status: UserStatuses.Active);

        Assert.True(await CreateRepository().SetStatusAsync(user.Uuid, UserStatuses.Disabled));
        Assert.Equal(UserStatuses.Disabled, (await ReloadUserAsync(user.Uuid))!.Status);
    }

    [SkippableFact]
    public async Task SetRoleAsync_UpdatesRole_ReturnsTrue()
    {
        Fixture.SkipIfNoDb();
        var user = await SeedUserAsync(role: UserRoles.User);

        Assert.True(await CreateRepository().SetRoleAsync(user.Uuid, UserRoles.Admin));
        Assert.Equal(UserRoles.Admin, (await ReloadUserAsync(user.Uuid))!.Role);
    }

    [SkippableFact]
    public async Task SetFieldAsync_UnknownUuid_ReturnsFalse()
    {
        Fixture.SkipIfNoDb();
        var repository = CreateRepository();

        Assert.False(await repository.SetTierAsync("no-such-uuid", UserTiers.Premium));
        Assert.False(await repository.SetStatusAsync("no-such-uuid", UserStatuses.Disabled));
        Assert.False(await repository.SetRoleAsync("no-such-uuid", UserRoles.Admin));
    }

    // ---- CountByRoleAsync (last-admin guard) --------------------------------------------------------

    [SkippableFact]
    public async Task CountByRoleAsync_CountsUsersOfRole_ByDelta()
    {
        Fixture.SkipIfNoDb();
        var repository = CreateRepository();
        var adminsBefore = await repository.CountByRoleAsync(UserRoles.Admin);
        var usersBefore = await repository.CountByRoleAsync(UserRoles.User);

        await SeedUserAsync(role: UserRoles.Admin);
        await SeedUserAsync(role: UserRoles.Admin);
        await SeedUserAsync(role: UserRoles.User);

        Assert.Equal(adminsBefore + 2, await repository.CountByRoleAsync(UserRoles.Admin));
        Assert.Equal(usersBefore + 1, await repository.CountByRoleAsync(UserRoles.User));
    }
}
