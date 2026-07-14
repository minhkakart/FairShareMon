namespace FairShareMonApi.Repositories.Admin;

/// <summary>
/// Repository-facing aggregate/query records for the M11 admin suite. These are NOT DTOs - they carry
/// account-metadata and tier-grant figures from the repositories up to the admin services, which map
/// them to the <c>Models/Admin</c> responses. Per the R10 privacy boundary, none of these ever carry
/// ledger data (members/expenses/events/shares/bank accounts).
/// </summary>

/// <summary>Filter/paging/sort inputs for the admin user listing (OQ7). All filters optional.</summary>
public sealed record AdminUserQuery(
    string? Tier,
    string? Status,
    string? Role,
    string? Search,
    int Page,
    int PageSize,
    string Sort,
    bool Descending);

/// <summary>One user's account metadata for the admin listing/detail (OQ7). NO ledger fields (R10).</summary>
public sealed record AdminUserAccount(
    ulong Id,
    string Uuid,
    string Username,
    string Tier,
    string Role,
    string Status,
    DateTime CreatedAt);

/// <summary>Per-user tier-grant summary stitched into the listing (grant count + most recent grant time).</summary>
public sealed record TierGrantSummary(
    ulong UserId,
    int GrantCount,
    DateTime? LastGrantAt);

/// <summary>Cross-user account-metadata metrics over <c>users</c> only (OQ6). NO ledger aggregates (R10).</summary>
public sealed record AdminMetricsAggregate(
    int TotalUsers,
    IReadOnlyList<CountByKey> TierDistribution,
    IReadOnlyList<CountByKey> RoleDistribution,
    IReadOnlyList<CountByKey> StatusDistribution,
    IReadOnlyList<PeriodCount> Signups);

/// <summary>A keyed count (e.g. tier -> count) for a distribution.</summary>
public sealed record CountByKey(string Key, int Count);

/// <summary>A time-bucket count (signups over time).</summary>
public sealed record PeriodCount(string PeriodLabel, int Count);

/// <summary>Revenue over <c>tier_grants</c> GRANT rows only (OQ14): per-bucket totals + grand totals + references.</summary>
public sealed record RevenueAggregate(
    IReadOnlyList<RevenueBucket> Buckets,
    decimal TotalRevenue,
    int GrantCount,
    IReadOnlyList<string> References);

/// <summary>One revenue bucket: label + summed GRANT amount + grant count.</summary>
public sealed record RevenueBucket(
    string PeriodLabel,
    decimal Total,
    int GrantCount);
