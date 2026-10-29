using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NamespaceRoot.ProductName.Common.Application.Serialization;

/// <summary>
/// One serialization setup for the whole platform.
/// </summary>
/// <remarks>
/// Every service reads these options rather than declaring its own. A service that configures serialization
/// itself will eventually differ - in an enum format, in a date format, in whether nulls are written - and
/// the client discovers the difference on whichever endpoint happens to be answered by that service.
///
/// It lives in the application layer rather than the web one because JSON is written outside the web
/// pipeline too, in messages and in stored copies of responses, and those have to match what a client
/// receives over HTTP.
/// </remarks>
public static class PlatformJson
{
    /// <summary>
    /// The options every service serializes with.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Build();

    /// <summary>
    /// Applies the shared options to a serializer configured elsewhere - the MVC one, the minimal-API one.
    /// </summary>
    public static void Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.WriteIndented = false;

        // The default encoder escapes anything non-ASCII, which turns names and addresses into \uXXXX
        // sequences: still valid JSON, unreadable in a log or a support ticket.
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

        // Enums travel as names, not as numbers. A number is meaningless in a log, and reordering the
        // enum silently changes the meaning of data already stored elsewhere.
        if (!options.Converters.OfType<JsonStringEnumConverter>().Any())
        {
            options.Converters.Add(new JsonStringEnumConverter());
        }

        if (!options.Converters.OfType<UtcDateTimeOffsetConverter>().Any())
        {
            options.Converters.Add(new UtcDateTimeOffsetConverter());
        }
    }

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions();
        Apply(options);

        return options;
    }
}
