using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Web.Swagger.Filters;

/// <summary>
/// Removes endpoints from versioned Swagger documents that don't belong to that version.
/// The "all" document is left unchanged.
/// </summary>
public class VersionedDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (swaggerDoc.Info.Version == "all") return;

        var version = swaggerDoc.Info.Version;
        var pathsToRemove = swaggerDoc.Paths
            .Where(p => ApiVersionHelper.ExtractVersionFromRoute(p.Key) != version)
            .Select(p => p.Key)
            .ToList();

        foreach (var path in pathsToRemove)
            swaggerDoc.Paths.Remove(path);
    }
}
