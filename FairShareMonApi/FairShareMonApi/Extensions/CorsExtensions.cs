using System.Net;

namespace FairShareMonApi.Extensions;

/// <summary>
/// Wires the default CORS policy (planning/cors-configuration.md). Ported from the sibling project
/// quick-ordering, adapted so the private/localhost/loopback auto-allow is <b>gated to Development
/// only</b>. Explicitly configured origins come from <c>App:AllowedOrigins</c> (mirroring the other
/// <c>App:</c> keys such as <c>App:DefaultCulture</c>) and are honored in every environment; local
/// origins are honored only when <c>Environment.IsDevelopment()</c> is true.
/// <para>
/// The policy uses <c>SetIsOriginAllowed</c> (a predicate that reflects the specific requesting
/// origin) together with <c>AllowCredentials</c>. This is the correct pairing for a credentialed API
/// with a dynamic origin list - <c>AllowAnyOrigin</c> cannot be combined with credentials. FairShareMon
/// carries an opaque Bearer token in the <c>Authorization</c> header (not cookies), so credentialed
/// CORS is safe here.
/// </para>
/// </summary>
public static class CorsExtensions
{
    /// <summary>Name of the single default CORS policy.</summary>
    public const string DefaultCorsPolicyName = "DefaultCors";

    /// <summary>Config key for the explicitly allowed origins list.</summary>
    public const string AllowedOriginsConfigKey = "App:AllowedOrigins";

    /// <summary>
    /// Registers the default CORS policy. <paramref name="allowLocalOrigins"/> should be
    /// <c>Environment.IsDevelopment()</c>: when true, any localhost/loopback/private-network origin is
    /// allowed on top of the configured list; when false, only the configured list is honored.
    /// </summary>
    public static IServiceCollection AddDefaultCorsPolicy(this IServiceCollection services, IConfiguration configuration, bool allowLocalOrigins)
    {
        var allowedOrigins = configuration.GetSection(AllowedOriginsConfigKey).Get<string[]>() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy(DefaultCorsPolicyName, policy =>
            {
                policy.SetIsOriginAllowed(origin => IsAllowedOrigin(origin, allowedOrigins, allowLocalOrigins))
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }

    /// <summary>
    /// Returns true if <paramref name="origin"/> is an HTTP(S) origin that either matches the configured
    /// list or - only when <paramref name="allowLocalOrigins"/> is true - is a localhost/loopback/private
    /// origin.
    /// </summary>
    public static bool IsAllowedOrigin(string? origin, IEnumerable<string>? allowedOrigins = null, bool allowLocalOrigins = false)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (!IsHttpOrigin(uri))
            return false;

        if (IsConfiguredOrigin(uri, allowedOrigins))
            return true;

        return allowLocalOrigins && IsLocalOrigin(uri);
    }

    private static bool IsConfiguredOrigin(Uri origin, IEnumerable<string>? allowedOrigins)
    {
        if (allowedOrigins is null)
            return false;

        var normalizedOrigin = NormalizeOrigin(origin);

        return allowedOrigins
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeOrigin)
            .Any(value => string.Equals(value, normalizedOrigin, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLocalOrigin(Uri origin)
    {
        var host = origin.Host;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var ipAddress))
            return false;

        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        return IsPrivateOrLoopbackAddress(ipAddress);
    }

    private static bool IsPrivateOrLoopbackAddress(IPAddress ipAddress)
    {
        if (IPAddress.IsLoopback(ipAddress))
            return true;

        return ipAddress.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => IsPrivateIpv4(ipAddress),
            System.Net.Sockets.AddressFamily.InterNetworkV6 => IsPrivateIpv6(ipAddress),
            _ => false
        };
    }

    private static bool IsPrivateIpv4(IPAddress ipAddress)
    {
        var bytes = ipAddress.GetAddressBytes();

        return bytes is [10, ..]
               || bytes is [172, >= 16 and <= 31, ..]
               || bytes is [192, 168, ..]
               || bytes is [127, ..];
    }

    private static bool IsPrivateIpv6(IPAddress ipAddress)
    {
        var bytes = ipAddress.GetAddressBytes();

        return bytes is [0xFC or 0xFD, ..]
               || bytes is [0xFE, var second, ..] && (second & 0xC0) == 0x80;
    }

    private static bool IsHttpOrigin(Uri origin) =>
        origin.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeOrigin(Uri origin)
    {
        var builder = new UriBuilder(origin)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    private static string NormalizeOrigin(string origin)
    {
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            ? NormalizeOrigin(uri)
            : origin.Trim();
    }
}
