using FairShareMonApi.Auth;

namespace FairShareMonApi.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IRequestTimeZone"/> used by the pure service unit tests (no HttpContext).
/// Returns a fixed <see cref="TimeZoneInfo"/> chosen by the test, so a service that threads
/// <see cref="IRequestTimeZone.Zone"/> (EventsService range normalization, ExportService formatting) can
/// be driven deterministically without the middleware/HttpContext pipeline.
/// </summary>
public sealed class TestRequestTimeZone(TimeZoneInfo zone) : IRequestTimeZone
{
    public TestRequestTimeZone() : this(TimeZoneInfo.Utc)
    {
    }

    public TimeZoneInfo Zone { get; set; } = zone;
}
