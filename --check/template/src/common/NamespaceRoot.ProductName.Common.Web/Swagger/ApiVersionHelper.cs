using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;

namespace NamespaceRoot.ProductName.Common.Web.Swagger;

/// <summary>
/// Discovers API versions from controller route attributes.
/// </summary>
public static partial class ApiVersionHelper
{
    /// <summary>
    /// Scans <paramref name="assembly"/> for controllers and returns every version their routes carry
    /// ("v1", "v2"), in numeric order: v2 before v10.
    /// </summary>
    public static IEnumerable<string> DiscoverAllVersions(Assembly assembly)
    {
        var versions = new HashSet<string>();

        var controllerTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(ControllerBase)));

        foreach (var controllerType in controllerTypes)
        {
            var route = controllerType.GetCustomAttribute<RouteAttribute>();
            if (route?.Template == null) continue;
            var version = ExtractVersionFromRoute(route.Template);
            if (version != null)
                versions.Add(version);
        }

        return versions.OrderBy(v => v.Length).ThenBy(v => v, StringComparer.Ordinal);
    }

    /// <summary>
    /// The version segment ("v1") of a route template or a document path, or <c>null</c> for a route
    /// without one. An unversioned route belongs to no versioned document; it appears only in "all".
    /// </summary>
    /// <remarks>
    /// Route attributes are written without a leading slash ("api/v1/orders") and document paths with
    /// one ("/api/v1/orders"); both match.
    /// </remarks>
    public static string? ExtractVersionFromRoute(string route)
    {
        if (string.IsNullOrEmpty(route)) return null;
        var match = VersionRegex().Match(route);
        return match.Success ? $"v{match.Groups[1].Value}" : null;
    }

    [GeneratedRegex(@"(?:^|/)api/v(\d+)(?:/|$)")]
    private static partial Regex VersionRegex();
}
