using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Repositories.Admin;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Append-only data access for the <c>tier_grants</c> table (M11). Grants are the immutable
/// grant/revoke trail and the revenue dashboard's sole data source (OQ14): rows are only ever
/// inserted, never updated. Revenue counts <c>GRANT</c> rows only; <c>REVOKE</c> rows (amount 0) never
/// count. This repository queries <c>tier_grants</c> only - never a ledger table (R10).
/// </summary>
public interface ITierGrantRepository : IBaseRepository, IQueryRepository<TierGrant>
{
    /// <summary>Appends one grant/revoke row (never updates an existing row).</summary>
    Task<TierGrant> AddAsync(TierGrant grant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically flips the target user's tier AND appends the grant/revoke row in ONE transaction
    /// (all-or-nothing, like expense+shares). Null when the user no longer exists.
    /// </summary>
    Task<TierGrant?> RecordAsync(string userUuid, string newTier, TierGrant grant, CancellationToken cancellationToken = default);

    /// <summary>The user's full grant history (most recent first) for the admin detail view.</summary>
    Task<IReadOnlyList<TierGrant>> ListByUserIdAsync(ulong userId, CancellationToken cancellationToken = default);

    /// <summary>Per-user grant-count + last-grant summaries for the listed users (stitched into the admin list).</summary>
    Task<IReadOnlyList<TierGrantSummary>> GetGrantSummariesAsync(IReadOnlyList<ulong> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revenue over GRANT rows in an inclusive <c>[from,to]</c> UTC range (either bound optional =
    /// all-time), bucketed by month (default) or day (OQ14). DB-side <c>GROUP BY</c> + <c>SUM</c>,
    /// mirroring the M7 Stats triad.
    /// </summary>
    Task<RevenueAggregate> GetRevenueAsync(DateTime? from, DateTime? to, string bucket, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(ITierGrantRepository))]
public sealed class TierGrantRepository(AppDbContext dbContext) : BaseRepository(dbContext), ITierGrantRepository
{
    public IQueryable<TierGrant> Query(bool tracking = false, bool includeDeleted = false) =>
        Query<TierGrant>(tracking, includeDeleted);

    public Task<TierGrant> AddAsync(TierGrant grant, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, _) =>
        {
            db.TierGrants.Add(grant);
            return await Task.FromResult(grant);
        }, cancellationToken);

    public Task<TierGrant?> RecordAsync(string userUuid, string newTier, TierGrant grant, CancellationToken cancellationToken = default) =>
        ExecuteTransactionAsync(async (db, transaction) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(existing => existing.Uuid == userUuid, cancellationToken);
            if (user is null)
            {
                transaction.NoCommit();
                return null;
            }

            user.Tier = newTier;
            db.TierGrants.Add(grant);
            return grant;
        }, cancellationToken);

    public Task<IReadOnlyList<TierGrant>> ListByUserIdAsync(ulong userId, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var rows = await Query()
                .Where(grant => grant.UserId == userId)
                .OrderByDescending(grant => grant.CreatedAt)
                .ToListAsync(ct);
            return (IReadOnlyList<TierGrant>)rows;
        }, cancellationToken);

    public Task<IReadOnlyList<TierGrantSummary>> GetGrantSummariesAsync(IReadOnlyList<ulong> userIds, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            if (userIds.Count == 0)
                return (IReadOnlyList<TierGrantSummary>)Array.Empty<TierGrantSummary>();

            var summaries = await Query()
                .Where(grant => userIds.Contains(grant.UserId) && grant.Action == TierGrantActions.Grant)
                .GroupBy(grant => grant.UserId)
                .Select(group => new TierGrantSummary(
                    group.Key,
                    group.Count(),
                    group.Max(grant => (DateTime?)grant.CreatedAt)))
                .ToListAsync(ct);
            return (IReadOnlyList<TierGrantSummary>)summaries;
        }, cancellationToken);

    public Task<RevenueAggregate> GetRevenueAsync(DateTime? from, DateTime? to, string bucket, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            // Only GRANT rows are revenue (OQ14); REVOKE rows (amount 0) never count.
            var grants = Query().Where(grant => grant.Action == TierGrantActions.Grant);
            if (from.HasValue)
                grants = grants.Where(grant => grant.CreatedAt >= from.Value);
            if (to.HasValue)
                grants = grants.Where(grant => grant.CreatedAt <= to.Value);

            var byDay = bucket == DashboardBuckets.Day;

            // DB-side GROUP BY year/month(/day) + SUM (mirrors StatsRepository).
            var grouped = await grants
                .GroupBy(grant => new
                {
                    grant.CreatedAt.Year,
                    grant.CreatedAt.Month,
                    Day = byDay ? grant.CreatedAt.Day : 1
                })
                .Select(group => new
                {
                    group.Key.Year,
                    group.Key.Month,
                    group.Key.Day,
                    Total = group.Sum(grant => grant.Amount),
                    Count = group.Count()
                })
                .ToListAsync(ct);

            var buckets = grouped
                .OrderBy(row => row.Year).ThenBy(row => row.Month).ThenBy(row => row.Day)
                .Select(row => new RevenueBucket(
                    byDay
                        ? $"{row.Year:D4}-{row.Month:D2}-{row.Day:D2}"
                        : $"{row.Year:D4}-{row.Month:D2}",
                    row.Total,
                    row.Count))
                .ToList();

            var totalRevenue = buckets.Sum(b => b.Total);
            var grantCount = buckets.Sum(b => b.GrantCount);

            var references = await grants
                .Where(grant => grant.Reference != null && grant.Reference != string.Empty)
                .OrderByDescending(grant => grant.CreatedAt)
                .Select(grant => grant.Reference!)
                .ToListAsync(ct);

            return new RevenueAggregate(buckets, totalRevenue, grantCount, references);
        }, cancellationToken);
}
