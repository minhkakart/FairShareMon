namespace FairShareMonApi.Constants;

/// <summary>
/// Time-bucket granularities for the admin metrics/revenue dashboards (M11, OQ14). <see cref="Month"/>
/// is the default; <see cref="Day"/> is the finer option. Both aggregate DB-side via <c>GROUP BY</c> on
/// the timestamp's year/month(/day) parts, mirroring the M7 Stats triad.
/// </summary>
public static class DashboardBuckets
{
    public const string Day = "day";

    public const string Month = "month";
}
