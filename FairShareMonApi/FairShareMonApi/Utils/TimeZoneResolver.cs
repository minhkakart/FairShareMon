using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;

namespace FairShareMonApi.Utils;

/// <summary>
/// Resolves a timezone string - either an IANA id (e.g. <c>Asia/Ho_Chi_Minh</c>) or a numeric UTC
/// offset (e.g. <c>+07:00</c>, <c>+7</c>, <c>-05:00</c>) - into a <see cref="TimeZoneInfo"/>,
/// cross-platform (Windows-vs-Linux id handling ported from quick-ordering). An invalid/unknown value
/// falls back silently to the configured app-default zone
/// (<c>App:DefaultTimeZone</c>, default <c>Asia/Ho_Chi_Minh</c>) per the timezone doc (D1) - it never
/// throws to the caller.
/// </summary>
public static class TimeZoneResolver
{
    /// <summary>Config key for the app-default presentation zone.</summary>
    public const string DefaultTimeZoneConfigKey = "App:DefaultTimeZone";

    /// <summary>Ultimate fallback zone id when config is missing/unresolvable.</summary>
    public const string FallbackTimeZoneId = "Asia/Ho_Chi_Minh";

    /// <summary>Key under which the resolved <see cref="TimeZoneInfo"/> is stashed in HttpContext.Items.</summary>
    public const string HttpContextItemsKey = "RequestTimeZone";

    // Preferred, stable IANA zones when resolving an ambiguous numeric offset (ported from quick-ordering).
    private static readonly ImmutableList<string> PreferredIanaIds =
    [
        "Asia/Bangkok", "Asia/Ho_Chi_Minh", "Asia/Jakarta",
        "Asia/Singapore", "Asia/Manila", "Asia/Kuala_Lumpur"
    ];

    /// <summary>
    /// Resolves <paramref name="value"/> (IANA id or numeric offset) to a zone; returns
    /// <paramref name="defaultZone"/> for null/blank/unparseable input. Never throws.
    /// </summary>
    public static TimeZoneInfo ResolveOrDefault(string? value, TimeZoneInfo defaultZone) =>
        TryResolve(value, out var zone) ? zone : defaultZone;

    /// <summary>
    /// Tries to resolve <paramref name="value"/> as an IANA (or Windows) id first, then as a numeric
    /// UTC offset. Returns false (with <paramref name="zone"/> = <see cref="TimeZoneInfo.Utc"/>) when
    /// the value is null/blank or cannot be interpreted as either.
    /// </summary>
    public static bool TryResolve(string? value, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();

        // IANA (or Windows) id: .NET 8 accepts both on either OS via ICU.
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(trimmed);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            // Not an id - fall through to offset parsing.
        }
        catch (InvalidTimeZoneException)
        {
            // Corrupt zone data - fall through to offset parsing.
        }

        var byOffset = ResolveByOffset(trimmed);
        if (byOffset is null)
            return false;

        zone = byOffset;
        return true;
    }

    /// <summary>
    /// Resolves the configured app-default zone (<c>App:DefaultTimeZone</c>, IANA id or numeric offset);
    /// falls back to <see cref="FallbackTimeZoneId"/> then <see cref="TimeZoneInfo.Utc"/>. Never throws.
    /// </summary>
    public static TimeZoneInfo GetDefaultZone(IConfiguration configuration)
    {
        var configured = configuration[DefaultTimeZoneConfigKey];
        if (TryResolve(configured, out var zone))
            return zone;
        if (TryResolve(FallbackTimeZoneId, out var fallback))
            return fallback;
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Reads the zone resolved once by <c>RequestTimeZoneMiddleware</c> from
    /// <see cref="HttpContextItemsKey"/>; falls back to the app-default when there is no HttpContext
    /// (background threads) or no resolved zone. Used by the scoped accessor and the singleton STJ
    /// converters so header parsing happens exactly once per request.
    /// </summary>
    public static TimeZoneInfo FromHttpContext(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        if (httpContextAccessor.HttpContext?.Items.TryGetValue(HttpContextItemsKey, out var value) == true
            && value is TimeZoneInfo zone)
            return zone;

        return GetDefaultZone(configuration);
    }

    private static TimeZoneInfo? ResolveByOffset(string offsetString)
    {
        var matches = FindTimeZonesByOffset(offsetString);
        if (matches.Count == 0)
            return null;

        // On Linux prefer a stable, common IANA zone; on Windows the enumerated zones have no IANA ids.
        if (!OperatingSystem.IsWindows())
            return matches.FirstOrDefault(tz => PreferredIanaIds.Contains(tz.Id))
                   ?? matches.FirstOrDefault(tz => tz.HasIanaId)
                   ?? matches[0];

        return matches[0];
    }

    private static List<TimeZoneInfo> FindTimeZonesByOffset(string offsetString)
    {
        if (!TryParseOffset(offsetString, out var offset))
            return [];

        var now = DateTime.UtcNow;
        return TimeZoneInfo.GetSystemTimeZones()
            .Where(tz => tz.BaseUtcOffset == offset || tz.GetUtcOffset(now) == offset)
            .ToList();
    }

    /// <summary>
    /// Parses a numeric UTC offset like <c>+7</c>, <c>7</c>, <c>-5</c>, <c>+07:00</c> into a
    /// <see cref="TimeSpan"/>. Deliberately does NOT use <c>TimeSpan.TryParse</c> (which reads a bare
    /// <c>"7"</c> as 7 days), so <c>+7</c> correctly means 7 hours. Valid range -12:00..+14:00.
    /// </summary>
    private static bool TryParseOffset(string offsetString, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;

        var normalized = offsetString;
        if (!normalized.StartsWith('+') && !normalized.StartsWith('-'))
            normalized = "+" + normalized;

        var sign = normalized.StartsWith('-') ? -1 : 1;
        var body = normalized[1..];
        var parts = body.Split(':');

        if (!int.TryParse(parts[0], out var hours))
            return false;

        var minutes = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;
        if (hours is < 0 or > 14 || minutes is < 0 or > 59)
            return false;

        offset = new TimeSpan(hours * sign, minutes * sign, 0);
        return offset >= TimeSpan.FromHours(-12) && offset <= TimeSpan.FromHours(14);
    }
}
