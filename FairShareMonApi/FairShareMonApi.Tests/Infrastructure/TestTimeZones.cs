namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Deterministic, cross-platform <see cref="TimeZoneInfo"/> values for the timezone-aware DateTime tests
/// (planning/timezone-aware-datetimes.md). Built with <see cref="TimeZoneInfo.CreateCustomTimeZone(string,TimeSpan,string,string)"/>
/// so they carry a FIXED offset with NO DST and never depend on the OS tz database being present - the
/// tests assert offset behavior, not a specific IANA id (which may be absent on a given OS).
/// <c>Asia/Ho_Chi_Minh</c> is itself a fixed +7 with no DST, so <see cref="Plus7"/> models it exactly.
/// </summary>
public static class TestTimeZones
{
    /// <summary>Fixed UTC+7 (models <c>Asia/Ho_Chi_Minh</c>, the app default, no DST).</summary>
    public static readonly TimeZoneInfo Plus7 =
        TimeZoneInfo.CreateCustomTimeZone("Test+07:00", TimeSpan.FromHours(7), "Test +07:00", "Test +07:00");

    /// <summary>Fixed UTC-5 (a non-default, negative offset for contrast).</summary>
    public static readonly TimeZoneInfo Minus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test-05:00", TimeSpan.FromHours(-5), "Test -05:00", "Test -05:00");

    /// <summary>UTC (zero offset).</summary>
    public static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
}
