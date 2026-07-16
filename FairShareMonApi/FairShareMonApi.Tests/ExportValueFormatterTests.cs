using FairShareMonApi.Services.Api.Export;
using FairShareMonApi.Tests.Infrastructure;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for <see cref="ExportValueFormatter"/> - the centralized money/date
/// stringifiers and M8's single most bug-prone point. Timezone-aware DateTimes (D3/D6) made both date
/// formatters take a <see cref="TimeZoneInfo"/> (the resolved request zone) instead of a hardcoded +7.
/// Proves the TWO distinct date rules: an INSTANT is converted UTC-&gt;request-zone and formatted
/// <c>dd/MM/yyyy HH:mm</c> (so a late-UTC instant can roll forward a day in a +7 viewer), whereas the
/// event CALENDAR range is a tz-normalized whole-day UTC boundary converted back to that SAME zone and
/// formatted <c>dd/MM/yyyy</c> - so an <c>end_date</c> normalized as the +7 end-of-day
/// (<c>16:59:59.999999Z</c>) keeps its own calendar day and never rolls to the next. Money is invariant
/// <c>0.00</c> - dot separator, two decimals, no grouping, including negatives and fractional cents.
/// </summary>
public class ExportValueFormatterTests
{
    private static readonly TimeZoneInfo Plus7 = TestTimeZones.Plus7;
    private static readonly TimeZoneInfo Utc = TestTimeZones.Utc;

    // ---- Instant (converted to the given zone, dd/MM/yyyy HH:mm) ----------------------------------

    [Fact]
    public void FormatInstant_ConvertsUtcToPlus7_AndRollsForwardAcrossMidnight()
    {
        var utc = new DateTime(2026, 3, 1, 18, 30, 0, DateTimeKind.Utc);

        // 18:30Z + 7h = 01:30 on the next calendar day.
        Assert.Equal("02/03/2026 01:30", ExportValueFormatter.FormatInstant(utc, Plus7));
    }

    [Fact]
    public void FormatInstant_MiddayUtc_StaysSameDayWithPlus7()
    {
        var utc = new DateTime(2026, 7, 14, 5, 0, 0, DateTimeKind.Utc);

        Assert.Equal("14/07/2026 12:00", ExportValueFormatter.FormatInstant(utc, Plus7));
    }

    [Fact]
    public void FormatInstant_Midnight_ShowsSevenAmSameDay()
    {
        var utc = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("03/03/2026 07:00", ExportValueFormatter.FormatInstant(utc, Plus7));
    }

    [Fact]
    public void FormatInstant_UtcZone_NoShift()
    {
        var utc = new DateTime(2026, 3, 1, 18, 30, 0, DateTimeKind.Utc);

        // The UTC viewer sees the raw instant, no roll-forward.
        Assert.Equal("01/03/2026 18:30", ExportValueFormatter.FormatInstant(utc, Utc));
    }

    // ---- Calendar date (converted to the given zone, dd/MM/yyyy) ----------------------------------

    [Fact]
    public void FormatCalendarDate_Plus7EndBoundary_KeepsOwnDay_NoDayRoll()
    {
        // The critical case: the whole-day end of 03/03 IN +7 is stored as 2026-03-03T16:59:59.999999Z.
        // Converted back to +7 it is 03/03 23:59:59.999999 - it must render 03/03, NOT roll to 04/03.
        var endBoundary = new DateTime(2026, 3, 3, 17, 0, 0, DateTimeKind.Utc).AddTicks(-10);

        Assert.Equal("03/03/2026", ExportValueFormatter.FormatCalendarDate(endBoundary, Plus7));
    }

    [Fact]
    public void FormatCalendarDate_Plus7StartBoundary_ShowsSameDay()
    {
        // The whole-day start of 01/03 IN +7 is stored as 2026-02-28T17:00:00Z; back in +7 it is 01/03 00:00.
        var startBoundary = new DateTime(2026, 2, 28, 17, 0, 0, DateTimeKind.Utc);

        Assert.Equal("01/03/2026", ExportValueFormatter.FormatCalendarDate(startBoundary, Plus7));
    }

    [Fact]
    public void FormatCalendarDate_SameStoredBoundary_RendersDifferentDayPerZone()
    {
        // A raw UTC-day end boundary: in UTC it is 03/03; a +7 viewer sees it roll to 04/03. This is why
        // the range must be normalized in - and rendered back in - the SAME zone (D3).
        var utcDayEnd = new DateTime(2026, 3, 3, 23, 59, 59, DateTimeKind.Utc).AddTicks(9_999_990);

        Assert.Equal("03/03/2026", ExportValueFormatter.FormatCalendarDate(utcDayEnd, Utc));
        Assert.Equal("04/03/2026", ExportValueFormatter.FormatCalendarDate(utcDayEnd, Plus7));
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
