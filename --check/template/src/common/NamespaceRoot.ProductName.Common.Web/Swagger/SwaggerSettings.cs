using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace NamespaceRoot.ProductName.Common.Web.Swagger;

/// <summary>
/// What the service's and the gateway's Swagger documents say beyond the code: the servers a reader
/// sends requests to, and what is taken out of the descriptions.
/// </summary>
public interface ISwaggerSettings
{
    /// <summary>
    /// The documents' <c>servers</c>, the first one the default. Empty means no <c>servers</c> at all, and
    /// the base URL is wherever the document was fetched from: right for a local run.
    /// </summary>
    IReadOnlyList<SwaggerServer> PublicServers { get; }

    /// <summary>
    /// Regular expressions whose matches are removed from the descriptions, such as the ids of a tracker
    /// that XML comments name and a reader of the document cannot open. Empty means nothing is removed.
    /// </summary>
    IReadOnlyList<string> ScrubPatterns { get; }
}

/// <summary>One server of the documents.</summary>
public sealed class SwaggerServer
{
    /// <summary>
    /// An absolute URL, scheme included. A host with a server variable instead was refused by Postman when it
    /// imported the document as OpenAPI 3.0, and importing it is what the servers are for.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>The name of the server in the picker of the Swagger page.</summary>
    public string? Description { get; init; }
}

/// <inheritdoc cref="ISwaggerSettings" />
public sealed class SwaggerSettings : ISwaggerSettings, IValidatableObject
{
    public const string SectionName = "Swagger";

    public List<SwaggerServer> PublicServers { get; init; } = [];

    public List<string> ScrubPatterns { get; init; } = [];

    IReadOnlyList<SwaggerServer> ISwaggerSettings.PublicServers => PublicServers;

    IReadOnlyList<string> ISwaggerSettings.ScrubPatterns => ScrubPatterns;

    // Checked here rather than with attributes on the items: the validation of options does not look
    // into the items of a list, so an attribute there would never run.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var server in PublicServers)
            if (!Uri.TryCreate(server.Url, UriKind.Absolute, out _))
                yield return new ValidationResult(
                    $"{SectionName}:PublicServers has '{server.Url}': a server is an absolute URL, scheme included.",
                    [nameof(PublicServers)]);

        foreach (var pattern in ScrubPatterns)
        {
            string? error = null;
            try { _ = new Regex(pattern); }
            catch (ArgumentException ex) { error = ex.Message; }
            if (error is not null)
                yield return new ValidationResult(
                    $"{SectionName}:ScrubPatterns has '{pattern}', which is not a regular expression: {error}",
                    [nameof(ScrubPatterns)]);
        }
    }
}
