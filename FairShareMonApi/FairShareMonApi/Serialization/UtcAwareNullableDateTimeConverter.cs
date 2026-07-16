using System.Text.Json;
using System.Text.Json.Serialization;
using FairShareMonApi.Localization;
using FairShareMonApi.Localization.Resources;
using FairShareMonApi.Utils;
using Microsoft.Extensions.Localization;

namespace FairShareMonApi.Serialization;

/// <summary>
/// Nullable variant of <see cref="UtcAwareDateTimeConverter"/>: a JSON <c>null</c> round-trips as
/// <c>null</c>; a present value applies the exact same request-zone read/write rules (see that type),
/// including localized parse errors.
/// </summary>
public sealed class UtcAwareNullableDateTimeConverter(
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    IStringLocalizerFactory? localizerFactory = null)
    : JsonConverter<DateTime?>
{
    // DI supplies the factory; when constructed without one (e.g. unit tests) fall back to the shared
    // localizer, which resolves the same resx family and honours CurrentUICulture.
    private readonly IStringLocalizer _localizer =
        localizerFactory?.Create(typeof(StringResources)) ?? SharedStringLocalizer.Instance;

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return RequestDateTimeSerializer.Read(ref reader, ResolveZone(), _localizer);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(RequestDateTimeSerializer.Write(value.Value, ResolveZone()));
    }

    private TimeZoneInfo ResolveZone() =>
        TimeZoneResolver.FromHttpContext(httpContextAccessor, configuration);
}
