using FairShareMonApi.Database.Conversions;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the EF <see cref="UtcDateTimeConverter"/> (planning/timezone-aware-datetimes.md,
/// Foundation step 2) - no DB. The read side (provider -&gt; model) stamps a materialized value with
/// <see cref="DateTimeKind.Utc"/> (fixing Pomelo's <c>datetime(6)</c> -&gt; <c>Unspecified</c> drift that
/// caused the M5 audit no-op bug), while the write side (model -&gt; provider) is an identity no-op so the
/// stored value and column definition are unchanged (no EF migration).
/// </summary>
public class UtcDateTimeConverterTests
{
    private readonly UtcDateTimeConverter _converter = new();

    [Fact]
    public void ReadSide_StampsKindUtc_PreservingTicks()
    {
        var fromDb = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Unspecified);

        var materialized = (DateTime)_converter.ConvertFromProvider(fromDb)!;

        Assert.Equal(DateTimeKind.Utc, materialized.Kind);
        Assert.Equal(fromDb.Ticks, materialized.Ticks); // same clock value, just re-labelled UTC
    }

    [Fact]
    public void WriteSide_IsIdentity_ValueAndTicksUnchanged()
    {
        var value = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

        var toDb = (DateTime)_converter.ConvertToProvider(value)!;

        Assert.Equal(value.Ticks, toDb.Ticks);
        Assert.Equal(value, toDb);
    }
}
