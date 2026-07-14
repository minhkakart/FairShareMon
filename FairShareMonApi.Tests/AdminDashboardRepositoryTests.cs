using FairShareMonApi.Constants;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Admin;
using FairShareMonApi.Tests.Infrastructure;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests (real MariaDB, skippable) for <see cref="AdminDashboardRepository.GetMetricsAsync"/>
/// (M11, OQ6): total-user, tier/role/status distributions, and signups-over-time buckets - all over the
/// <c>users</c> table ONLY (no ledger table is queried, R10). The distribution counts are global, so they
/// are asserted by DELTA around a known seed; the signup buckets are asserted precisely by seeding users
/// with a far-future <c>created_at</c> and querying that isolated window.
/// </summary>
[Collection("AuthIntegration")]
public class AdminDashboardRepositoryTests(DatabaseFixture fixture) : AdminDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private AdminDashboardRepository CreateRepository() => new(CreateContext());

    private static int CountFor(IReadOnlyList<CountByKey> distribution, string key) =>
        distribution.SingleOrDefault(entry => entry.Key == key)?.Count ?? 0;

    [SkippableFact]
    public async Task GetMetricsAsync_TotalAndDistributions_ReflectSeededUsers_ByDelta()
    {
        Fixture.SkipIfNoDb();
        var repository = CreateRepository();
        var before = await repository.GetMetricsAsync(null, null, DashboardBuckets.Month);

        // A known cohort: 2 premium, 1 admin, 1 disabled (roles/status overlap with tiers is fine).
        await SeedUserAsync(tier: UserTiers.Premium);
        await SeedUserAsync(tier: UserTiers.Premium, role: UserRoles.Admin);
        await SeedUserAsync(status: UserStatuses.Disabled);
        await SeedUserAsync();

        var after = await repository.GetMetricsAsync(null, null, DashboardBuckets.Month);

        Assert.Equal(before.TotalUsers + 4, after.TotalUsers);
        Assert.Equal(CountFor(before.TierDistribution, UserTiers.Premium) + 2, CountFor(after.TierDistribution, UserTiers.Premium));
        Assert.Equal(CountFor(before.RoleDistribution, UserRoles.Admin) + 1, CountFor(after.RoleDistribution, UserRoles.Admin));
        Assert.Equal(CountFor(before.StatusDistribution, UserStatuses.Disabled) + 1, CountFor(after.StatusDistribution, UserStatuses.Disabled));
    }

    [SkippableFact]
    public async Task GetMetricsAsync_SignupBuckets_GroupByMonth_OverIsolatedWindow()
    {
        Fixture.SkipIfNoDb();
        // Far-future created_at so only these users fall inside the queried window.
        await SeedUserAsync(createdAt: new DateTime(2095, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        await SeedUserAsync(createdAt: new DateTime(2095, 1, 20, 0, 0, 0, DateTimeKind.Utc));
        await SeedUserAsync(createdAt: new DateTime(2095, 2, 5, 0, 0, 0, DateTimeKind.Utc));

        var from = new DateTime(2095, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2095, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var metrics = await CreateRepository().GetMetricsAsync(from, to, DashboardBuckets.Month);

        Assert.Equal(2, metrics.Signups.Count);
        Assert.Equal(2, metrics.Signups.Single(bucket => bucket.PeriodLabel == "2095-01").Count);
        Assert.Equal(1, metrics.Signups.Single(bucket => bucket.PeriodLabel == "2095-02").Count);
    }

    [SkippableFact]
    public async Task GetMetricsAsync_SignupBuckets_GroupByDay_OverIsolatedWindow()
    {
        Fixture.SkipIfNoDb();
        await SeedUserAsync(createdAt: new DateTime(2094, 6, 1, 8, 0, 0, DateTimeKind.Utc));
        await SeedUserAsync(createdAt: new DateTime(2094, 6, 1, 22, 0, 0, DateTimeKind.Utc));
        await SeedUserAsync(createdAt: new DateTime(2094, 6, 2, 8, 0, 0, DateTimeKind.Utc));

        var from = new DateTime(2094, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2094, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var metrics = await CreateRepository().GetMetricsAsync(from, to, DashboardBuckets.Day);

        Assert.Equal(2, metrics.Signups.Single(bucket => bucket.PeriodLabel == "2094-06-01").Count);
        Assert.Equal(1, metrics.Signups.Single(bucket => bucket.PeriodLabel == "2094-06-02").Count);
    }
}
