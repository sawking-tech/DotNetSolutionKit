using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NamespaceRoot.ProductName.Common.Web.Swagger;

namespace NamespaceRoot.ProductName.Common.Web.Gateway;

/// <summary>
/// The Swagger documents of the services behind the gateway, shown in the gateway's own Swagger page.
/// </summary>
/// <param name="Name">The cluster of the service, as the page lists it.</param>
/// <param name="Url">Where the gateway serves the service's document.</param>
public sealed record ServiceSwaggerDocument(string Name, string Url);

/// <summary>
/// A route of the gateway to a service, as the document of that service is checked against it.
/// </summary>
/// <param name="Path">The route's <c>Match:Path</c> template.</param>
/// <param name="Methods">Its <c>Match:Methods</c>; empty for every method.</param>
public sealed record GatewayRoute(string Path, IReadOnlyList<string> Methods);

/// <summary>
/// Finds the services' documents in the gateway's routes and serves each one as a caller of the gateway
/// sees it.
/// </summary>
/// <remarks>
/// A service is listed through a route of its own in <c>ReverseProxy:Routes</c>:
/// <c>/swagger/&lt;cluster&gt;/{**rest}</c> to the cluster, with the path transformed to
/// <c>/swagger/{**rest}</c>. The list is the routes; there is no second setting to keep in step with them.
/// <para>
/// The gateway does not hand the service's document on as it is. It takes the service's <c>all</c>
/// document from the cluster's first destination and keeps the paths and methods the cluster's other
/// routes accept: an endpoint no route reaches answers 404 through the gateway, and listing it only sends
/// a caller after it. It sets the gateway's own <c>servers</c>. A route with a transform of the path is
/// not followed: a path the service sees differently from the caller is beyond what this check reads.
/// </para>
/// </remarks>
public static partial class GatewaySwagger
{
    private const string ServiceDocument = "swagger/all/swagger.json";

    [GeneratedRegex(@"^/swagger/(?<cluster>[^/{}]+)/\{\*\*rest\}$")]
    private static partial Regex SwaggerRoute();

    private static readonly string[] OperationKeys = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    /// <summary>Where the gateway serves the document of <paramref name="cluster"/>.</summary>
    public static string DocumentUrl(string cluster) => $"/swagger-services/{cluster}.json";

    public static IReadOnlyList<ServiceSwaggerDocument> FromRoutes(IConfiguration reverseProxy) =>
        Clusters(reverseProxy).Select(cluster => new ServiceSwaggerDocument(cluster, DocumentUrl(cluster))).ToList();

    /// <summary>
    /// Lists the services' documents of <paramref name="reverseProxy"/> on the gateway's Swagger page.
    /// </summary>
    public static IServiceCollection AddGatewaySwagger(this IServiceCollection services, IConfiguration reverseProxy)
    {
        foreach (var document in FromRoutes(reverseProxy))
            services.AddSingleton(document);
        services.AddHttpClient(nameof(GatewaySwagger));
        return services;
    }

    /// <summary>
    /// Serves the documents <see cref="AddGatewaySwagger"/> listed. Outside Production only, as the
    /// services serve theirs.
    /// </summary>
    public static WebApplication MapGatewaySwagger(this WebApplication app, IConfiguration reverseProxy)
    {
        if (app.Environment.IsProduction())
            return app;

        foreach (var cluster in Clusters(reverseProxy))
        {
            var destination = reverseProxy.GetSection($"Clusters:{cluster}:Destinations").GetChildren()
                .Select(d => d["Address"]).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
            if (destination is null)
                continue;
            var routes = RoutesOf(reverseProxy, cluster);
            var source = new Uri(new Uri(destination.TrimEnd('/') + "/"), ServiceDocument);

            app.MapGet(DocumentUrl(cluster), async (IHttpClientFactory clients, ISwaggerSettings settings, CancellationToken ct) =>
                {
                    string json;
                    try
                    {
                        json = await clients.CreateClient(nameof(GatewaySwagger)).GetStringAsync(source, ct);
                    }
                    catch (HttpRequestException ex)
                    {
                        return Results.Problem(
                            statusCode: StatusCodes.Status503ServiceUnavailable,
                            title: $"The document of {cluster} is not available",
                            detail: ex.Message);
                    }

                    var document = AsSeenThroughGateway(JsonNode.Parse(json)!.AsObject(), routes, SwaggerServers.ToJson(settings));
                    return Results.Content(document.ToJsonString(), "application/json");
                })
                .ExcludeFromDescription()
                .AllowAnonymous();
        }

        return app;
    }

    /// <summary>
    /// The service's document as a caller of the gateway sees it: only the paths and methods
    /// <paramref name="routes"/> accept, and <paramref name="servers"/> in place of the service's servers.
    /// </summary>
    public static JsonObject AsSeenThroughGateway(JsonObject document, IReadOnlyList<GatewayRoute> routes, JsonArray? servers)
    {
        var matchers = routes
            .Select(r => (Matcher: new TemplateMatcher(TemplateParser.Parse(r.Path.TrimStart('/')), new RouteValueDictionary()), r.Methods))
            .ToList();

        var kept = new JsonObject();
        foreach (var (path, item) in document["paths"]?.AsObject() ?? [])
        {
            if (item is not JsonObject operations)
                continue;
            var accepting = matchers.Where(m => m.Matcher.TryMatch(new PathString(path), new RouteValueDictionary())).ToList();
            if (accepting.Count == 0)
                continue;

            var copy = operations.DeepClone().AsObject();
            foreach (var method in OperationKeys)
                if (copy.ContainsKey(method) && !accepting.Any(m => m.Methods.Count == 0 ||
                        m.Methods.Contains(method, StringComparer.OrdinalIgnoreCase)))
                    copy.Remove(method);
            if (OperationKeys.Any(copy.ContainsKey))
                kept[path] = copy;
        }

        // The service's own servers point past the gateway; without servers of the gateway's, the base URL is
        // the gateway the document came from.
        var result = document.DeepClone().AsObject();
        result["paths"] = kept;
        result.Remove("servers");
        if (servers is not null)
            result["servers"] = servers.DeepClone();
        return result;
    }

    private static IEnumerable<string> Clusters(IConfiguration reverseProxy) =>
        reverseProxy.GetSection("Routes").GetChildren()
            .Select(route => SwaggerRoute().Match(route["Match:Path"] ?? string.Empty))
            .Where(match => match.Success)
            .Select(match => match.Groups["cluster"].Value)
            .Distinct();

    /// <summary>
    /// The routes to <paramref name="cluster"/> its document is checked against: all but its swagger route and
    /// the routes that transform the path.
    /// </summary>
    public static IReadOnlyList<GatewayRoute> RoutesOf(IConfiguration reverseProxy, string cluster) =>
        reverseProxy.GetSection("Routes").GetChildren()
            .Where(route => route["ClusterId"] == cluster)
            .Where(route => !SwaggerRoute().IsMatch(route["Match:Path"] ?? string.Empty))
            .Where(route => !route.GetSection("Transforms").GetChildren().Any(t => t["PathPattern"] is not null ||
                t["PathRemovePrefix"] is not null || t["PathPrefix"] is not null || t["PathSet"] is not null))
            .Where(route => !string.IsNullOrWhiteSpace(route["Match:Path"]))
            .Select(route => new GatewayRoute(
                route["Match:Path"]!,
                route.GetSection("Match:Methods").GetChildren().Select(m => m.Value!).ToList()))
            .ToList();
}
