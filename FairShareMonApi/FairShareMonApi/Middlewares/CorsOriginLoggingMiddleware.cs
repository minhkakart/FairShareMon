using FairShareMonApi.Extensions;
using Microsoft.Net.Http.Headers;

namespace FairShareMonApi.Middlewares;

/// <summary>
/// Logs a warning for any cross-origin request whose <c>Origin</c> the CORS policy will reject.
/// Sits deliberately BEFORE <c>UseCors</c>, because <c>CorsMiddleware</c> short-circuits a preflight
/// and nothing downstream ever runs.
/// <para>
/// This exists because a rejected preflight is otherwise invisible: <c>CorsMiddleware</c> answers
/// <b>every</b> preflight with <c>204 No Content</c> once a policy is registered and merely omits the
/// <c>Access-Control-Allow-Origin</c> header when the origin fails - so a rejection and a success are
/// byte-identical in an access log. The framework's own <c>OriginNotAllowed</c> message is
/// Information-level under <c>Microsoft.AspNetCore.Cors.Infrastructure.CorsService</c>, which
/// <c>Logging:LogLevel:Microsoft.AspNetCore = Warning</c> and the NLog <c>Microsoft.*</c> blackhole
/// rule both discard. A production login failure on Safari therefore needed Cloudflare tunnel logs to
/// diagnose (planning/https-scheme-enforcement.md).
/// </para>
/// <para>
/// The usual cause is a <b>scheme</b> mismatch: <see cref="CorsExtensions"/> compares
/// <c>Uri.GetLeftPart(UriPartial.Authority)</c>, so <c>http://host</c> and <c>https://host</c> are
/// different origins and a page served over plaintext fails an https-only allowlist.
/// </para>
/// <para>
/// The verdict comes from <see cref="CorsExtensions.IsAllowedOrigin"/> - the same predicate the policy
/// itself uses - so this log can never disagree with the actual decision. The response is never
/// touched; <c>UseCors</c> remains the sole authority.
/// </para>
/// </summary>
public sealed class CorsOriginLoggingMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<CorsOriginLoggingMiddleware> logger)
{
    // Read once: the middleware is a singleton and the policy reads the same values at startup.
    private readonly string[] _allowedOrigins =
        configuration.GetSection(CorsExtensions.AllowedOriginsConfigKey).Get<string[]>() ?? [];

    private readonly bool _allowLocalOrigins = environment.IsDevelopment();

    public async Task InvokeAsync(HttpContext context)
    {
        var origin = context.Request.Headers[HeaderNames.Origin].ToString();

        // No Origin header = same-origin request; CORS never applies, so stay silent.
        if (!string.IsNullOrEmpty(origin)
            && !CorsExtensions.IsAllowedOrigin(origin, _allowedOrigins, _allowLocalOrigins))
        {
            logger.LogWarning(
                "CORS rejected origin {Origin} for {Method} {Path}; the browser will block this request. "
                + "Allowed origins: {AllowedOrigins}. The match includes the scheme, so http:// and https:// differ.",
                origin,
                context.Request.Method,
                context.Request.Path,
                AllowedOriginsDescription);
        }

        await next(context);
    }

    private string AllowedOriginsDescription =>
        _allowedOrigins.Length == 0 ? "(none configured)" : string.Join(", ", _allowedOrigins);
}
