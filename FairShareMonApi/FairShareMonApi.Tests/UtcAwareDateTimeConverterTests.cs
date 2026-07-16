using System.Globalization;
using System.Text.Json;
using FairShareMonApi.Serialization;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the global System.Text.Json <see cref="UtcAwareDateTimeConverter"/> and its
/// nullable sibling (planning/timezone-aware-datetimes.md D2/D6) - no HTTP pipeline. The per-request zone
/// is driven through a stubbed <see cref="IHttpContextAccessor"/> whose <c>HttpContext.Items</c> carries
/// the resolved zone (exactly what <c>RequestTimeZoneMiddleware</c> populates). Proves:
/// <list type="bullet">
/// <item>WRITE: a UTC value is converted to the request zone and emitted as ISO-8601 WITH offset (same
/// absolute instant, viewer-zone offset); a UTC viewer gets <c>+00:00</c>.</item>
/// <item>READ: an offset/Z-bearing token is honored as sent and converted to UTC; a naive token is
/// interpreted in the request zone then converted to UTC; both carry <see cref="DateTimeKind.Utc"/>.</item>
/// <item>Missing zone (no HttpContext item) falls back to the configured app-default zone.</item>
/// <item>Nullable: JSON null round-trips as null.</item>
/// </list>
/// </summary>
public class UtcAwareDateTimeConverterTests
{
    private static IHttpContextAccessor AccessorWithZone(TimeZoneInfo? zone)
    {
        var context = new DefaultHttpContext();
        if (zone is not null)
            context.Items[TimeZoneResolver.HttpContextItemsKey] = zone;
        return new HttpContextAccessor { HttpContext = context };
    }

    private static IConfiguration ConfigWithDefault(string? defaultZone)
    {
        var dict = new Dictionary<string, string?>();
        if (defaultZone is not null)
            dict["App:DefaultTimeZone"] = defaultZone;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static JsonSerializerOptions OptionsFor(TimeZoneInfo? zone, string? defaultZone = null)
    {
        var accessor = AccessorWithZone(zone);
        var config = ConfigWithDefault(defaultZone);
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcAwareDateTimeConverter(accessor, config));
        options.Converters.Add(new UtcAwareNullableDateTimeConverter(accessor, config));
        return options;
    }

    private static DateTimeOffset ParseOffset(string json)
    {
        var raw = JsonSerializer.Deserialize<string>(json)!; // unwrap the JSON string literal
        return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    // ---- Write ------------------------------------------------------------------------------------

    [Fact]
    public void Write_UtcValue_InPlus7Zone_EmitsOffsetAndSameInstant()
    {
        var options = OptionsFor(TestTimeZones.Plus7);
        var value = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(value, options);
        var parsed = ParseOffset(json);

        Assert.Equal(TimeSpan.FromHours(7), parsed.Offset);
        Assert.Equal(new DateTimeOffset(value), parsed); // same absolute instant
    }

    [Fact]
    public void Write_UtcValue_InUtcZone_EmitsZeroOffset()
    {
        var options = OptionsFor(TestTimeZones.Utc);
        var value = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

        var parsed = ParseOffset(JsonSerializer.Serialize(value, options));

        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(new DateTimeOffset(value), parsed);
    }

    [Fact]
    public void Write_NoZoneInContext_FallsBackToConfiguredDefault()
    {
        var options = OptionsFor(zone: null, defaultZone: "+00:00");
        var value = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

        var parsed = ParseOffset(JsonSerializer.Serialize(value, options));

        Assert.Equal(TimeSpan.Zero, parsed.Offset); // app-default fallback
    }

    // ---- Read -------------------------------------------------------------------------------------

    [Fact]
    public void Read_OffsetBearingToken_IsHonoredAndConvertedToUtc()
    {
        var options = OptionsFor(TestTimeZones.Plus7); // request zone irrelevant when the token carries an offset

        var result = JsonSerializer.Deserialize<DateTime>("\"2026-07-14T00:00:00+07:00\"", options);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(new DateTime(2026, 7, 13, 17, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void Read_ZuluToken_IsHonoredAsUtc()
    {
        var options = OptionsFor(TestTimeZones.Plus7);

        var result = JsonSerializer.Deserialize<DateTime>("\"2026-07-14T12:00:00Z\"", options);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void Read_NaiveToken_IsInterpretedInRequestZone_ThenConvertedToUtc()
    {
        var options = OptionsFor(TestTimeZones.Plus7);

        var result = JsonSerializer.Deserialize<DateTime>("\"2026-07-14T00:00:00\"", options);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(new DateTime(2026, 7, 13, 17, 0, 0, DateTimeKind.Utc), result); // 00:00 +7 -> 17:00Z prior day
    }

    [Fact]
    public void Read_NaiveToken_NoZoneInContext_UsesConfiguredDefaultZone()
    {
        var options = OptionsFor(zone: null, defaultZone: "+00:00");

        var result = JsonSerializer.Deserialize<DateTime>("\"2026-07-14T00:00:00\"", options);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc), result); // interpreted as UTC (default)
    }

    // ---- Nullable ---------------------------------------------------------------------------------

    [Fact]
    public void Nullable_Null_RoundTripsAsNull()
    {
        var options = OptionsFor(TestTimeZones.Plus7);

        Assert.Equal("null", JsonSerializer.Serialize((DateTime?)null, options));
        Assert.Null(JsonSerializer.Deserialize<DateTime?>("null", options));
    }

    [Fact]
    public void Nullable_Value_AppliesSameRequestZoneRules()
    {
        var options = OptionsFor(TestTimeZones.Plus7);
        DateTime? value = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);

        var parsed = ParseOffset(JsonSerializer.Serialize(value, options));
        Assert.Equal(TimeSpan.FromHours(7), parsed.Offset);

        var read = JsonSerializer.Deserialize<DateTime?>("\"2026-07-14T00:00:00\"", options);
        Assert.Equal(new DateTime(2026, 7, 13, 17, 0, 0, DateTimeKind.Utc), read);
        Assert.Equal(DateTimeKind.Utc, read!.Value.Kind);
    }
}
