using FairShareMonApi.Constants;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests (real MariaDB, skippable) for <see cref="TierGrantRepository"/> (M11). Proves
/// <c>RecordAsync</c> flips the target's tier AND appends the grant row in ONE atomic transaction (a
/// forced CHECK-constraint failure leaves NEITHER - the §4.3 <c>amount &gt;= 0</c> guard is a real DB
/// constraint), grant history is per-user newest-first, and revenue is a DB-side GROUP BY/SUM over GRANT
/// rows only (REVOKE excluded, amount 0) across an inclusive UTC range, bucketed by month/day, and
/// user-agnostic. Revenue tests bound their range to a far-future window so only their own seeded rows
/// fall in (the revenue query is global by design); every seeded row is swept on dispose.
/// </summary>
[Collection("AuthIntegration")]
public class TierGrantRepositoryTests(DatabaseFixture fixture) : AdminDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private TierGrantRepository CreateRepository() => new(CreateContext());

    private static TierGrant NewGrant(User target, User admin, string action, string tier, decimal amount, string? reference = null) => new()
    {
        UserId = target.Id,
        UserUsername = target.Username,
        Tier = tier,
        Action = action,
        Amount = amount,
        Currency = "VND",
        Reference = reference,
        GrantedByUserId = admin.Id,
        GrantedByUsername = admin.Username
    };

    // ---- RecordAsync atomicity ----------------------------------------------------------------------

    [SkippableFact]
    public async Task RecordAsync_Grant_FlipsTierToPremium_AndAppendsGrantRow_Atomically()
    {
        Fixture.SkipIfNoDb();
        var admin = await SeedUserAsync(role: UserRoles.Admin);
        var target = await SeedUserAsync(tier: UserTiers.Free);

        var grant = NewGrant(target, admin, TierGrantActions.Grant, UserTiers.Premium, 199_000m, "TT-1");
        var recorded = await CreateRepository().RecordAsync(target.Uuid, UserTiers.Premium, grant);

        Assert.NotNull(recorded);
        Assert.Equal(UserTiers.Premium, (await ReloadUserAsync(target.Uuid))!.Tier); // tier flipped
        var rows = await CreateRepository().ListByUserIdAsync(target.Id);
        var row = Assert.Single(rows);
        Assert.Equal(TierGrantActions.Grant, row.Action);
        Assert.Equal(199_000m, row.Amount);
    }

    [SkippableFact]
    public async Task RecordAsync_Revoke_FlipsTierToFree_AndAppendsRevokeRow()
    {
        Fixture.SkipIfNoDb();
        var admin = await SeedUserAsync(role: UserRoles.Admin);
        var target = await SeedUserAsync(tier: UserTiers.Premium);

        var grant = NewGrant(target, admin, TierGrantActions.Revoke, UserTiers.Free, 0m);
        await CreateRepository().RecordAsync(target.Uuid, UserTiers.Free, grant);

        Assert.Equal(UserTiers.Free, (await ReloadUserAsync(target.Uuid))!.Tier);
        Assert.Equal(TierGrantActions.Revoke, Assert.Single(await CreateRepository().ListByUserIdAsync(target.Id)).Action);
    }

    [SkippableFact]
    public async Task RecordAsync_ForcedCheckFailure_RollsBackTierAndRow()
    {
        Fixture.SkipIfNoDb();
        var admin = await SeedUserAsync(role: UserRoles.Admin);
        var target = await SeedUserAsync(tier: UserTiers.Free);

        // A negative amount violates ck_tier_grants_amount_non_negative -> the INSERT fails -> the whole
        // transaction (tier flip + row) rolls back. (Amount is validated in the service; here we hit the DB CHECK.)
        var badGrant = NewGrant(target, admin, TierGrantActions.Grant, UserTiers.Premium, -1m);

        await Assert.ThrowsAnyAsync<Exception>(() => CreateRepository().RecordAsync(target.Uuid, UserTiers.Premium, badGrant));

        Assert.Equal(UserTiers.Free, (await ReloadUserAsync(target.Uuid))!.Tier); // NOT flipped
        Assert.Empty(await CreateRepository().ListByUserIdAsync(target.Id));       // NO row left behind
    }

    [SkippableFact]
    public async Task RecordAsync_UnknownUser_ReturnsNull_WritesNothing()
    {
        Fixture.SkipIfNoDb();
        var admin = await SeedUserAsync(role: UserRoles.Admin);
        var ghostTarget = await SeedUserAsync(); // used only to shape a valid grant object

        var grant = NewGrant(ghostTarget, admin, TierGrantActions.Grant, UserTiers.Premium, 1m);
        var recorded = await CreateRepository().RecordAsync("no-such-user-uuid", UserTiers.Premium, grant);

        Assert.Null(recorded);
        Assert.Empty(await CreateRepository().ListByUserIdAsync(ghostTarget.Id));
    }

    // ---- Grant history ------------------------------------------------------------------------------

    [SkippableFact]
    public async Task ListByUserIdAsync_ReturnsUserHistoryNewestFirst_ScopedToUser()
    {
        Fixture.SkipIfNoDb();
        var admin = await SeedUserAsync(role: UserRoles.Admin);
        var target = await SeedUserAsync();
        var other = await SeedUserAsync();

        var older = new DateTime(2099, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2099, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        await SeedGrantAsync(target, admin, TierGrantActions.Grant, 100m, older);
        await SeedGrantAsync(target, admin, TierGrantActions.Revoke, 0m, newer, tier: UserTiers.Free);
        await SeedGrantAsync(other, admin, TierGrantActions.Grant, 999m, newer); // another user - excluded

        var history = await CreateRepository().ListByUserIdAsync(target.Id);

        Assert.Equal(2, history.Count);
        Assert.True(history[0].CreatedAt >= history[1].CreatedAt); // newest first
        Assert.Equal(TierGrantActions.Revoke, history[0].Action);
    }

    // ---- Revenue (GRANT-only, buckets, inclusive range, user-agnostic) ------------------------------

    [SkippableFact]
    public async Task GetRevenueAsync_MonthBuckets_SumsGrantRowsOnly_ExcludesRevoke_UserAgnostic()
    {
        Fixture.SkipIfNoDb();
        var admin = await SeedUserAsync(role: UserRoles.Admin);
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync();

        // Far-future window so only these rows fall in the (global) revenue query.
        var jan = new DateTime(2099, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2099, 2, 10, 0, 0, 0, DateTimeKind.Utc);
        await SeedGrantAsync(userA, admin, TierGrantActions.Grant, 100_000m, jan, reference: "R1");
        await SeedGrantAsync(userB, admin, TierGrantActions.Grant, 50_000m, jan, reference: "R2"); // user-agnostic sum
        await SeedGrantAsync(userA, admin, TierGrantActions.Grant, 200_000m, feb, reference: "R3");
        await SeedGrantAsync(userA, admin, TierGrantActions.Revoke, 0m, feb, tier: UserTiers.Free); // never revenue

        var from = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2099, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var revenue = await CreateRepository().GetRevenueAsync(from, to, DashboardBuckets.Month);

        Assert.Equal(350_000m, revenue.TotalRevenue); // 100k + 50k + 200k; REVOKE excluded
        Assert.Equal(3, revenue.GrantCount);          // 3 GRANT rows, REVOKE not counted
        Assert.Equal(2, revenue.Buckets.Count);
        Assert.Equal(150_000m, revenue.Buckets.Single(bucket => bucket.PeriodLabel == "2099-01").Total);
        Assert.Equal(200_000m, revenue.Buckets.Single(bucket => bucket.PeriodLabel == "2099-02").Total);
        Assert.Equal(3, revenue.References.Count);    // R1, R2, R3 (references from GRANT rows)
    }

    [SkippableFact]
    public async Task GetRevenueAsync_DayBuckets_GroupsByDay()
    {
        Fixture.SkipIfNoDb();
        var admin = await SeedUserAsync(role: UserRoles.Admin);
        var user = await SeedUserAsync();

        var day1 = new DateTime(2098, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        var day1Later = new DateTime(2098, 3, 1, 20, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2098, 3, 2, 9, 0, 0, DateTimeKind.Utc);
        await SeedGrantAsync(user, admin, TierGrantActions.Grant, 10m, day1);
        await SeedGrantAsync(user, admin, TierGrantActions.Grant, 20m, day1Later);
        await SeedGrantAsync(user, admin, TierGrantActions.Grant, 30m, day2);

        var from = new DateTime(2098, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2098, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        var revenue = await CreateRepository().GetRevenueAsync(from, to, DashboardBuckets.Day);

        Assert.Equal(2, revenue.Buckets.Count);
        Assert.Equal(30m, revenue.Buckets.Single(bucket => bucket.PeriodLabel == "2098-03-01").Total); // 10 + 20
        Assert.Equal(30m, revenue.Buckets.Single(bucket => bucket.PeriodLabel == "2098-03-02").Total);
    }

    [SkippableFact]
    public async Task GetRevenueAsync_RangeIsInclusiveOfBothBounds()
    {
        Fixture.SkipIfNoDb();
        var admin = await SeedUserAsync(role: UserRoles.Admin);
        var user = await SeedUserAsync();

        var from = new DateTime(2097, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2097, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        await SeedGrantAsync(user, admin, TierGrantActions.Grant, 11m, from);                                  // on the lower bound
        await SeedGrantAsync(user, admin, TierGrantActions.Grant, 22m, to);                                    // on the upper bound
        await SeedGrantAsync(user, admin, TierGrantActions.Grant, 99m, from.AddDays(-1));                      // just before -> excluded
        await SeedGrantAsync(user, admin, TierGrantActions.Grant, 99m, to.AddDays(1));                         // just after -> excluded

        var revenue = await CreateRepository().GetRevenueAsync(from, to, DashboardBuckets.Month);

        Assert.Equal(33m, revenue.TotalRevenue); // both bounds included, out-of-range excluded
        Assert.Equal(2, revenue.GrantCount);
    }

    // ---- Grant summaries (for the admin listing) ---------------------------------------------------

    [SkippableFact]
    public async Task GetGrantSummariesAsync_ReturnsPerUserGrantCountAndLastGrant_GrantRowsOnly()
    {
        Fixture.SkipIfNoDb();
        var admin = await SeedUserAsync(role: UserRoles.Admin);
        var user = await SeedUserAsync();

        var first = new DateTime(2096, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var last = new DateTime(2096, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedGrantAsync(user, admin, TierGrantActions.Grant, 100m, first);
        await SeedGrantAsync(user, admin, TierGrantActions.Grant, 100m, last);
        await SeedGrantAsync(user, admin, TierGrantActions.Revoke, 0m, last.AddDays(1), tier: UserTiers.Free); // not a grant

        var summaries = await CreateRepository().GetGrantSummariesAsync([user.Id]);

        var summary = Assert.Single(summaries);
        Assert.Equal(2, summary.GrantCount); // REVOKE excluded
        Assert.Equal(last, summary.LastGrantAt);
    }

    [SkippableFact]
    public async Task GetGrantSummariesAsync_EmptyInput_ReturnsEmpty()
    {
        Fixture.SkipIfNoDb();
        Assert.Empty(await CreateRepository().GetGrantSummariesAsync([]));
    }
}
