using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Repositories.Admin;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Repositories;

/// <summary>
/// Read-only cross-user account-metadata metrics over the <c>users</c> table ONLY (M11, OQ6). Mirrors
/// the M7 Stats triad's DB-side <c>GROUP BY</c>/<c>COUNT</c> shape, but is deliberately unscoped-by-user
/// (admin dashboards are cross-user by design) - which is safe ONLY because it touches no ledger table.
/// <b>No ledger aggregate of any kind is produced (R10).</b>
/// </summary>
public interface IAdminDashboardRepository : IBaseRepository
{
    /// <summary>
    /// Total users, tier/role/status distributions, and signups over an optional inclusive
    /// <c>[from,to]</c> UTC range bucketed by month (default) or day. Over <c>users</c> only (R10).
    /// </summary>
    Task<AdminMetricsAggregate> GetMetricsAsync(DateTime? from, DateTime? to, string bucket, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IAdminDashboardRepository))]
public sealed class AdminDashboardRepository(AppDbContext dbContext) : BaseRepository(dbContext), IAdminDashboardRepository
{
    public Task<AdminMetricsAggregate> GetMetricsAsync(DateTime? from, DateTime? to, string bucket, CancellationToken cancellationToken = default) =>
        ExecuteQueryAsync(async (_, ct) =>
        {
            var users = Query<User>();

            var totalUsers = await users.CountAsync(ct);

            var tierDistribution = await users
                .GroupBy(user => user.Tier)
                .Select(group => new CountByKey(group.Key, group.Count()))
                .ToListAsync(ct);

            var roleDistribution = await users
                .GroupBy(user => user.Role)
                .Select(group => new CountByKey(group.Key, group.Count()))
                .ToListAsync(ct);

            var statusDistribution = await users
                .GroupBy(user => user.Status)
                .Select(group => new CountByKey(group.Key, group.Count()))
                .ToListAsync(ct);

            var signups = Query<User>();
            if (from.HasValue)
                signups = signups.Where(user => user.CreatedAt >= from.Value);
            if (to.HasValue)
                signups = signups.Where(user => user.CreatedAt <= to.Value);

            var byDay = bucket == DashboardBuckets.Day;
            var groupedSignups = await signups
                .GroupBy(user => new
                {
                    user.CreatedAt.Year,
                    user.CreatedAt.Month,
                    Day = byDay ? user.CreatedAt.Day : 1
                })
                .Select(group => new
                {
                    group.Key.Year,
                    group.Key.Month,
                    group.Key.Day,
                    Count = group.Count()
                })
                .ToListAsync(ct);

            var signupBuckets = groupedSignups
                .OrderBy(row => row.Year).ThenBy(row => row.Month).ThenBy(row => row.Day)
                .Select(row => new PeriodCount(
                    byDay
                        ? $"{row.Year:D4}-{row.Month:D2}-{row.Day:D2}"
                        : $"{row.Year:D4}-{row.Month:D2}",
                    row.Count))
                .ToList();

            return new AdminMetricsAggregate(
                totalUsers,
                tierDistribution,
                roleDistribution,
                statusDistribution,
                signupBuckets);
        }, cancellationToken);
}
