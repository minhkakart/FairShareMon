using FairShareMonApi.Utils;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="TimeZoneResolver"/> (planning/timezone-aware-datetimes.md D1) - no DB,
/// no HttpContext. Proves an IANA id resolves; a numeric UTC offset (<c>+07:00</c>, <c>+7</c>, bare
/// <c>7</c>, <c>-05:00</c>, <c>-5</c>) resolves; unknown/blank/out-of-range values fail
/// <see cref="TimeZoneResolver.TryResolve"/> and fall back to the supplied default via
/// <see cref="TimeZoneResolver.ResolveOrDefault"/>; and <see cref="TimeZoneResolver.GetDefaultZone"/>
/// reads <c>App:DefaultTimeZone</c> and silently falls back to Asia/Ho_Chi_Minh when unset. Assertions
/// target OFFSET behavior, not a specific OS zone id (cross-platform: a numeric offset may resolve to any
/// zone carrying that offset).
/// </summary>
public class TimeZoneResolverTests
{
    private static readonly DateTime Instant = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>True when <paramref name="zone"/> carries <paramref name="hours"/> either as its base
    /// offset or its current offset (mirrors the resolver's own matching, DST-robust).</summary>
    private static bool HasOffset(TimeZoneInfo zone, double hours)
    {
        var target = TimeSpan.FromHours(hours);
        return zone.BaseUtcOffset == target
            || zone.GetUtcOffset(Instant) == target
            || zone.GetUtcOffset(DateTime.UtcNow) == target;
    }

    [Fact]
    public void TryResolve_IanaId_Resolves()
    {
        var ok = TimeZoneResolver.TryResolve("Asia/Ho_Chi_Minh", out var zone);

        Assert.True(ok);
        Assert.True(HasOffset(zone, 7)); // Asia/Ho_Chi_Minh is a fixed +7 (no DST)
    }

    [Theory]
    [InlineData("+07:00", 7)]
    [InlineData("+7", 7)]
    [InlineData("7", 7)]      // bare, no sign -> treated as +7 (NOT 7 days)
    [InlineData("+05:30", 5.5)]
    [InlineData("-05:00", -5)]
    [InlineData("-5", -5)]
    [InlineData("+00:00", 0)]
    public void TryResolve_NumericOffset_ResolvesToZoneWithThatOffset(string value, double hours)
    {
        var ok = TimeZoneResolver.TryResolve(value, out var zone);

        Assert.True(ok);
        Assert.True(HasOffset(zone, hours), $"resolved zone {zone.Id} does not carry offset {hours}h");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    [InlineData("garbage")]
    [InlineData("+99")]   // out of the -12..+14 range
    [InlineData("-13")]
    public void TryResolve_UnknownOrBlankOrOutOfRange_ReturnsFalse(string? value)
    {
        var ok = TimeZoneResolver.TryResolve(value, out _);

        Assert.False(ok);
    }

    [Fact]
    public void ResolveOrDefault_InvalidValue_ReturnsProvidedDefault()
    {
        var fallback = TimeZoneInfo.CreateCustomTimeZone("Fallback+3", TimeSpan.FromHours(3), "F+3", "F+3");

        var zone = TimeZoneResolver.ResolveOrDefault("Not/AZone", fallback);

        Assert.Same(fallback, zone);
    }

    [Fact]
    public void ResolveOrDefault_ValidValue_ReturnsResolvedNotDefault()
    {
        var fallback = TimeZoneInfo.CreateCustomTimeZone("Fallback+3", TimeSpan.FromHours(3), "F+3", "F+3");

        var zone = TimeZoneResolver.ResolveOrDefault("+07:00", fallback);

        Assert.NotSame(fallback, zone);
        Assert.True(HasOffset(zone, 7));
    }

    [Fact]
    public void GetDefaultZone_ReadsAppDefaultTimeZoneFromConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:DefaultTimeZone"] = "+00:00" })
            .Build();

        var zone = TimeZoneResolver.GetDefaultZone(config);

        Assert.True(HasOffset(zone, 0));
    }

    [Fact]
    public void GetDefaultZone_MissingConfig_FallsBackToHoChiMinhPlus7()
    {
        var config = new ConfigurationBuilder().Build(); // no App:DefaultTimeZone

        var zone = TimeZoneResolver.GetDefaultZone(config);

        Assert.True(HasOffset(zone, 7)); // silent fallback to Asia/Ho_Chi_Minh
    }
}
