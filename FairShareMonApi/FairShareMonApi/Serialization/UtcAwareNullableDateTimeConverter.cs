using System.Text.Json;
using System.Text.Json.Serialization;
using FairShareMonApi.Utils;

namespace FairShareMonApi.Serialization;

/// <summary>
/// Nullable variant of <see cref="UtcAwareDateTimeConverter"/>: a JSON <c>null</c> round-trips as
/// <c>null</c>; a present value applies the exact same request-zone read/write rules (see that type).
/// </summary>
public sealed class UtcAwareNullableDateTimeConverter(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        return RequestDateTimeSerializer.Read(ref reader, ResolveZone());
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
