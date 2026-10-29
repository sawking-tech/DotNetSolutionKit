using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NamespaceRoot.ProductName.Common.Application.Serialization;

/// <summary>
/// Writes every <see cref="DateTimeOffset"/> as UTC with a trailing <c>Z</c>
/// ("2026-01-19T10:30:00Z"), and converts anything read to UTC.
/// </summary>
/// <remarks>
/// Without this the wire format follows whatever offset the server happens to run in, and two services in
/// two regions describe the same instant differently. Clients then compare timestamps as strings, and the
/// comparison is wrong in a way that only shows up across a daylight-saving boundary.
/// </remarks>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    // Fractional seconds are written only when present: a timestamp with no sub-second part should not
    // gain a ".000" that suggests a precision the value does not have.
    private const string Format = "yyyy-MM-ddTHH:mm:ss.FFFZ";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString()
                    ?? throw new JsonException("Expected a date and time, found null.");

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
