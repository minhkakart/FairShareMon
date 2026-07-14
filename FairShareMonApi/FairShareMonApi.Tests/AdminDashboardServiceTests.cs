using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Admin;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Repositories.Admin;
using FairShareMonApi.Services.Api.Admin;
using FairShareMonApi.Validators.Admin;
using FluentValidation;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for <see cref="AdminDashboardService"/> over fake repositories + a real
/// AutoMapper/validators. Proves the metrics aggregate (users-only distributions + signup buckets) and
/// the revenue aggregate (GRANT-derived buckets, totals, references) map faithfully onto their response
/// DTOs, that the request's <c>From</c>/<c>To</c>/<c>Bucket</c> echo back on the response, that the
/// revenue query is issued for the requested bucket, and that bad range/bucket inputs fail validation
/// (1001). The GRANT-only vs REVOKE-excluded revenue rule is a repository concern, asserted in
/// <c>TierGrantRepositoryTests</c>.
/// </summary>
public class AdminDashboardServiceTests
{
    private readonly FakeDashboardRepository _dashboard = new();
    private readonly FakeRevenueRepository _grants = new();
    private readonly IMapper _mapper = new MapperConfiguration(config => config.AddProfile<AdminProfile>()).CreateMapper();

    private AdminDashboardService CreateService() => new(
        _dashboard, _grants, _mapper, new AdminMetricsRequestValidator(), new RevenueRequestValidator());

    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    // ---- Metrics ------------------------------------------------------------------------------------

    [Fact]
    public async Task GetMetricsAsync_MapsDistributionsAndSignups_EchoesRange()
    {
        _dashboard.Aggregate = new AdminMetricsAggregate(
            TotalUsers: 5,
            TierDistribution: [new CountByKey(UserTiers.Free, 3), new CountByKey(UserTiers.Premium, 2)],
            RoleDistribution: [new CountByKey(UserRoles.User, 4), new CountByKey(UserRoles.Admin, 1)],
            StatusDistribution: [new CountByKey(UserStatuses.Active, 4), new CountByKey(UserStatuses.Disabled, 1)],
            Signups: [new PeriodCount("2026-01", 2), new PeriodCount("2026-02", 3)]);

        var response = await CreateService().GetMetricsAsync(new AdminMetricsRequest { From = From, To = To, Bucket = DashboardBuckets.Month });

        Assert.Equal(5, response.TotalUsers);
        Assert.Equal(2, response.TierDistribution.Single(count => count.Key == UserTiers.Premium).Count);
        Assert.Equal(1, response.RoleDistribution.Single(count => count.Key == UserRoles.Admin).Count);
        Assert.Equal(1, response.StatusDistribution.Single(count => count.Key == UserStatuses.Disabled).Count);
        Assert.Equal(2, response.Signups.Count);
        Assert.Equal(From, response.From);
        Assert.Equal(To, response.To);
    }

    [Fact]
    public async Task GetMetricsAsync_FromAfterTo_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().GetMetricsAsync(new AdminMetricsRequest { From = To, To = From }));
    }

    // ---- Revenue ------------------------------------------------------------------------------------

    [Fact]
    public async Task GetRevenueAsync_MapsBucketsTotalsReferences_EchoesRangeAndBucket()
    {
        _grants.Aggregate = new RevenueAggregate(
            Buckets: [new RevenueBucket("2026-01", 199_000m, 1), new RevenueBucket("2026-02", 398_000m, 2)],
            TotalRevenue: 597_000m,
            GrantCount: 3,
            References: ["TT1", "TT2", "TT3"]);

        var response = await CreateService().GetRevenueAsync(new RevenueRequest { From = From, To = To, Bucket = DashboardBuckets.Month });

        Assert.Equal(597_000m, response.TotalRevenue);
        Assert.Equal(3, response.GrantCount);
        Assert.Equal(2, response.Buckets.Count);
        Assert.Equal(398_000m, response.Buckets.Single(bucket => bucket.PeriodLabel == "2026-02").Total);
        Assert.Equal(3, response.References.Count);
        Assert.Equal(DashboardBuckets.Month, response.Bucket);
        Assert.Equal(From, response.From);
        Assert.Equal(To, response.To);
        Assert.Equal(DashboardBuckets.Month, _grants.RequestedBucket); // the requested bucket is passed to the repo
    }

    [Fact]
    public async Task GetRevenueAsync_DayBucket_PassedThrough()
    {
        _grants.Aggregate = new RevenueAggregate([], 0m, 0, []);

        var response = await CreateService().GetRevenueAsync(new RevenueRequest { Bucket = DashboardBuckets.Day });

        Assert.Equal(DashboardBuckets.Day, _grants.RequestedBucket);
        Assert.Equal(DashboardBuckets.Day, response.Bucket);
        Assert.Empty(response.Buckets);
    }

    [Fact]
    public async Task GetRevenueAsync_UnknownBucket_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().GetRevenueAsync(new RevenueRequest { Bucket = "week" }));
    }

    // ---- Fakes --------------------------------------------------------------------------------------

    private sealed class FakeDashboardRepository : IAdminDashboardRepository
    {
        public AdminMetricsAggregate Aggregate { get; set; } = new(0, [], [], [], []);

        public Task<AdminMetricsAggregate> GetMetricsAsync(DateTime? from, DateTime? to, string bucket, CancellationToken cancellationToken = default) =>
            Task.FromResult(Aggregate);

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRevenueRepository : ITierGrantRepository
    {
        public RevenueAggregate Aggregate { get; set; } = new([], 0m, 0, []);
        public string? RequestedBucket { get; private set; }

        public Task<RevenueAggregate> GetRevenueAsync(DateTime? from, DateTime? to, string bucket, CancellationToken cancellationToken = default)
        {
            RequestedBucket = bucket;
            return Task.FromResult(Aggregate);
        }

        public Task<TierGrant> AddAsync(TierGrant grant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TierGrant?> RecordAsync(string userUuid, string newTier, TierGrant grant, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TierGrant>> ListByUserIdAsync(ulong userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TierGrantSummary>> GetGrantSummariesAsync(IReadOnlyList<ulong> userIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<TierGrant> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();
        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
