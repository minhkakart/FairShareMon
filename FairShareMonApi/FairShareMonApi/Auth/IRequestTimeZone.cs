using DiDecoration.Attributes;
using FairShareMonApi.Utils;

namespace FairShareMonApi.Auth;

/// <summary>Accessor for the current request's resolved presentation timezone (mirrors
/// <see cref="IContextAuthenticated"/>). Reads the zone resolved once by
/// <c>RequestTimeZoneMiddleware</c> from <c>HttpContext.Items</c>, falling back to the app-default when
/// there is no HttpContext (background threads) or no resolved zone.</summary>
public interface IRequestTimeZone
{
    /// <summary>The resolved <see cref="TimeZoneInfo"/> for the current request (never null).</summary>
    TimeZoneInfo Zone { get; }
}

[ScopedService(typeof(IRequestTimeZone))]
public sealed class RequestTimeZone(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    : IRequestTimeZone
{
    public TimeZoneInfo Zone => TimeZoneResolver.FromHttpContext(httpContextAccessor, configuration);
}
