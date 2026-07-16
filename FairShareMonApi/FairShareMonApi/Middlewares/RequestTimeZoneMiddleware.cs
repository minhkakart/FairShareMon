using FairShareMonApi.Utils;

namespace FairShareMonApi.Middlewares;

/// <summary>
/// Resolves the request's presentation timezone ONCE from the <c>X-Time-Zone</c> header (an IANA id or
/// a numeric UTC offset; missing/invalid -&gt; app-default, silent fallback per D1) and stashes the
/// resolved <see cref="TimeZoneInfo"/> in <c>HttpContext.Items</c>. Both the singleton STJ converters
/// (via <see cref="IHttpContextAccessor"/>) and the scoped <see cref="Auth.IRequestTimeZone"/> read
/// that single resolution, so header parsing happens exactly once per request.
/// </summary>
public sealed class RequestTimeZoneMiddleware(RequestDelegate next)
{
    /// <summary>Request header carrying the viewer's timezone (IANA id or numeric UTC offset).</summary>
    public const string HeaderName = "X-Time-Zone";

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        var headerValue = context.Request.Headers[HeaderName].ToString();
        var defaultZone = TimeZoneResolver.GetDefaultZone(configuration);

        context.Items[TimeZoneResolver.HttpContextItemsKey] =
            TimeZoneResolver.ResolveOrDefault(headerValue, defaultZone);

        await next(context);
    }
}
