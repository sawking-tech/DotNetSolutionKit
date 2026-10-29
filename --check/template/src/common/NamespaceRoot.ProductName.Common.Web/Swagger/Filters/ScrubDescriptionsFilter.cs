using System.Text.RegularExpressions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Web.Swagger.Filters;

/// <summary>
/// Removes the matches of <see cref="ISwaggerSettings.ScrubPatterns"/> from the descriptions of the
/// document, such as the ids of a tracker.
/// </summary>
/// <remarks>
/// An XML comment next to the code names the task a decision came from, and Swashbuckle copies the comment
/// into the document word for word, so the reader of the contract got references to a tracker they cannot
/// open. Removed on the way out, the code keeps where its decisions came from and the reader gets the
/// prose, and nobody has to remember a rule while writing a comment.
/// </remarks>
internal sealed class ScrubDescriptionsFilter(ISwaggerSettings settings) : IDocumentFilter
{
    // Deep enough for any model; $ref cycles are legal in OpenAPI, and a model referring to itself would
    // otherwise be walked for ever.
    private const int MaxDepth = 12;

    private readonly Regex[] _patterns = [.. settings.ScrubPatterns.Select(p => new Regex(p, RegexOptions.CultureInvariant))];

    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        if (_patterns.Length == 0)
            return;

        if (document.Info is { } info)
            info.Description = Scrub(info.Description);
        foreach (var tag in document.Tags ?? [])
            tag.Description = Scrub(tag.Description);
        foreach (var path in document.Paths?.Values ?? Enumerable.Empty<OpenApiPathItem>())
        {
            path.Summary = Scrub(path.Summary);
            path.Description = Scrub(path.Description);
            foreach (var operation in path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
                ScrubOperation(operation);
        }
        foreach (var schema in document.Components?.Schemas?.Values ?? Enumerable.Empty<OpenApiSchema>())
            ScrubSchema(schema, 0);
    }

    private void ScrubOperation(OpenApiOperation operation)
    {
        operation.Summary = Scrub(operation.Summary);
        operation.Description = Scrub(operation.Description);
        foreach (var parameter in operation.Parameters ?? [])
        {
            parameter.Description = Scrub(parameter.Description);
            if (parameter.Schema is { } schema)
                ScrubSchema(schema, 0);
        }
        if (operation.RequestBody is { } body)
            body.Description = Scrub(body.Description);
        foreach (var response in operation.Responses?.Values ?? Enumerable.Empty<OpenApiResponse>())
            response.Description = Scrub(response.Description);
    }

    private void ScrubSchema(OpenApiSchema schema, int depth)
    {
        if (depth > MaxDepth)
            return;
        schema.Description = Scrub(schema.Description);
        foreach (var property in schema.Properties?.Values ?? Enumerable.Empty<OpenApiSchema>())
            ScrubSchema(property, depth + 1);
        if (schema.Items is { } items)
            ScrubSchema(items, depth + 1);
        foreach (var part in (schema.AllOf ?? []).Concat(schema.OneOf ?? []).Concat(schema.AnyOf ?? []))
            ScrubSchema(part, depth + 1);
    }

    private string? Scrub(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        foreach (var pattern in _patterns)
            text = pattern.Replace(text, string.Empty);
        // What a match in the middle of a sentence leaves: " ." and double spaces.
        text = Regex.Replace(text, @"[ \t]+([.,;:])", "$1");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        return text.Trim();
    }
}
