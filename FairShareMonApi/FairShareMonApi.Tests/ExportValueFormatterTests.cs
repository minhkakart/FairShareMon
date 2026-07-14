using FairShareMonApi.Services.Api.Export;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for <see cref="ExportValueFormatter"/> - the centralized money/date
/// stringifiers and the milestone's single most bug-prone point (M8, OQ5/OQ6). Proves the TWO distinct
/// date rules: an INSTANT is shifted UTC-&gt;UTC+7 (fixed, no DST) and formatted <c>dd/MM/yyyy HH:mm</c>
/// (so a late-UTC instant rolls forward a day), whereas the event CALENDAR range is formatted
/// <c>dd/MM/yyyy</c> straight from the stored UTC date component with NO +7 shift (so an <c>end_date</c>
/// at <c>23:59:59.999999Z</c> keeps its own calendar day and never rolls to the next). Money is invariant
/// <c>0.00</c> - dot separator, two decimals, no grouping, including negatives and fractional cents.
/// </summary>
public class ExportValueFormatterTests
{
    // ---- Instant (+7, dd/MM/yyyy HH:mm) ------------------------------------------------------------

    [Fact]
    public void FormatInstant_ConvertsUtcToPlus7_AndRollsForwardAcrossMidnight()
    {
        var utc = new DateTime(2026, 3, 1, 18, 30, 0, DateTimeKind.Utc);

        // 18:30Z + 7h = 01:30 on the next calendar day.
        Assert.Equal("02/03/2026 01:30", ExportValueFormatter.FormatInstant(utc));
    }

    [Fact]
    public void FormatInstant_MiddayUtc_StaysSameDayWithPlus7()
    {
        var utc = new DateTime(2026, 7, 14, 5, 0, 0, DateTimeKind.Utc);

        Assert.Equal("14/07/2026 12:00", ExportValueFormatter.FormatInstant(utc));
    }

    [Fact]
    public void FormatInstant_Midnight_ShowsSevenAmSameDay()
    {
        var utc = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("03/03/2026 07:00", ExportValueFormatter.FormatInstant(utc));
    }

    // ---- Calendar date (NO shift, dd/MM/yyyy) ----------------------------------------------------

    [Fact]
    public void FormatCalendarDate_EndBoundary_KeepsOwnDay_NoDayRoll()
    {
        // The critical case: an end_date at 23:59:59.999999Z must NOT roll to 04/03/2026.
        var endBoundary = new DateTime(2026, 3, 3, 23, 59, 59, DateTimeKind.Utc).AddTicks(9_999_990);

        Assert.Equal("03/03/2026", ExportValueFormatter.FormatCalendarDate(endBoundary));
    }

    [Fact]
    public void FormatCalendarDate_StartBoundary_ShowsSameDay()
    {
        var startBoundary = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("01/03/2026", ExportValueFormatter.FormatCalendarDate(startBoundary));
    }

    // ---- Money (invariant 0.00) ------------------------------------------------------------------

    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(800000, "800000.00")]
    [InlineData(-200000, "-200000.00")]
    public void FormatMoney_WholeAndNegative_InvariantTwoDecimalsNoGrouping(decimal amount, string expected)
    {
        Assert.Equal(expected, ExportValueFormatter.FormatMoney(amount));
    }

    [Fact]
    public void FormatMoney_FractionalCents_UsesDotSeparator()
    {
        Assert.Equal("266.67", ExportValueFormatter.FormatMoney(266.67m));
    }
}
