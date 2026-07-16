using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FairShareMonApi.Utils;

namespace FairShareMonApi.Serialization;

/// <summary>
/// Global System.Text.Json converter that presents every <see cref="DateTime"/> in the viewer's
/// request zone while storage stays UTC (see planning/timezone-aware-datetimes.md, D2/D6).
/// <list type="bullet">
/// <item><b>Write:</b> treat the value as UTC -&gt; convert to the request zone -&gt; emit ISO-8601 WITH
/// offset (e.g. <c>2026-07-14T07:00:00.0000000+07:00</c>; a UTC viewer gets <c>+00:00</c>).</item>
/// <item><b>Read:</b> an offset/Z-bearing token is honored as sent and converted to UTC; a naive token
/// is interpreted in the request zone then converted to UTC. The result carries
/// <see cref="DateTimeKind.Utc"/>.</item>
/// </list>
/// This is a singleton converter, so it reads the per-request zone via <see cref="IHttpContextAccessor"/>
/// (resolved once by <c>RequestTimeZoneMiddleware</c>); it falls back to the app-default zone when there
/// is no HttpContext, so serialization never throws.
/// </summary>
public sealed class UtcAwareDateTimeConverter(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        RequestDateTimeSerializer.Read(ref reader, ResolveZone());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(RequestDateTimeSerializer.Write(value, ResolveZone()));

    private TimeZoneInfo ResolveZone() =>
        TimeZoneResolver.FromHttpContext(httpContextAccessor, configuration);
}

/// <summary>
/// Shared read/write logic for the UTC-aware <see cref="DateTime"/> converters (non-nullable and
/// nullable), so both apply exactly the same request-zone rules.
/// </summary>
internal static class RequestDateTimeSerializer
{
    /// <summary>UTC instant -&gt; request zone -&gt; ISO-8601 round-trip string WITH offset.</summary>
    public static string Write(DateTime value, TimeZoneInfo zone)
    {
        var utc = EnsureUtc(value);
        var inZone = TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), zone);
        return inZone.ToString("o", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads an ISO-8601 string: offset/Z-bearing -&gt; converted to UTC as sent; naive -&gt; interpreted
    /// in <paramref name="zone"/> then converted to UTC. Always returns <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    public static DateTime Read(ref Utf8JsonReader reader, TimeZoneInfo zone)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Giá trị ngày giờ phải là chuỗi ISO-8601.");

        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            throw new JsonException("Giá trị ngày giờ không hợp lệ.");

        if (HasExplicitOffset(raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return dto.UtcDateTime;

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var naive))
        {
            var unspecified = DateTime.SpecifyKind(naive, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        }

        throw new JsonException($"Giá trị ngày giờ không hợp lệ: {raw}");
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>
    /// True when the string carries an explicit zone (a trailing <c>Z</c> or a <c>+hh:mm</c>/<c>-hh:mm</c>
    /// offset in the time component). A date-only value (no <c>T</c>) is treated as naive so its date
    /// separators are not mistaken for an offset sign.
    /// </summary>
    private static bool HasExplicitOffset(string raw)
    {
        var tIndex = raw.IndexOf('T');
        if (tIndex < 0)
            return false;

        var timePart = raw[(tIndex + 1)..];
        return timePart.IndexOf('Z') >= 0 || timePart.IndexOf('z') >= 0
            || timePart.IndexOf('+') >= 0 || timePart.IndexOf('-') >= 0;
    }
}
