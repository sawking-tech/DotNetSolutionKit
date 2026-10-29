using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using NamespaceRoot.ProductName.Common.Web.Gateway;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// The services' Swagger documents the gateway lists come from its /swagger/&lt;cluster&gt;/ routes, and each
/// shows what a caller of the gateway can reach.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class GatewaySwaggerTests
{
    private static IConfiguration ReverseProxy(params (string Route, string Path)[] routes) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(routes.Select(r => new KeyValuePair<string, string?>($"Routes:{r.Route}:Match:Path", r.Path)))
            .Build();

    private static IConfiguration ReverseProxy(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static JsonObject Document(params string[] paths)
    {
        var items = new JsonObject();
        foreach (var path in paths)
            items[path] = new JsonObject { ["get"] = new JsonObject(), ["post"] = new JsonObject() };
        return new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["servers"] = new JsonArray(new JsonObject { ["url"] = "http://orders:8080" }),
            ["paths"] = items
        };
    }

    private static string[] Paths(JsonObject document) =>
        document["paths"]!.AsObject().Select(p => p.Key).ToArray();

    [Test]
    public void Each_swagger_route_lists_its_service()
    {
        var documents = GatewaySwagger.FromRoutes(ReverseProxy(
            ("orders", "/api/{version}/orders/{**rest}"),
            ("orders-swagger", "/swagger/orders/{**rest}"),
            ("billing-swagger", "/swagger/billing/{**rest}")));

        documents.ShouldBe([
            new ServiceSwaggerDocument("orders", "/swagger-services/orders.json"),
            new ServiceSwaggerDocument("billing", "/swagger-services/billing.json")
        ], ignoreOrder: true);
    }

    [Test]
    public void A_gateway_without_swagger_routes_lists_nothing()
    {
        GatewaySwagger.FromRoutes(ReverseProxy(("orders", "/api/{version}/orders/{**rest}"))).ShouldBeEmpty();
    }

    [Test(Description = "A path no route of the cluster reaches answers 404 through the gateway, so it is not shown")]
    public void Only_the_paths_a_route_reaches_are_kept()
    {
        var routes = new[] { new GatewayRoute("/api/{version}/orders/{**rest}", []) };

        var document = GatewaySwagger.AsSeenThroughGateway(
            Document("/api/v1/orders", "/api/v1/orders/{id}", "/api/v1/features"), routes, servers: null);

        Paths(document).ShouldBe(["/api/v1/orders", "/api/v1/orders/{id}"], ignoreOrder: true);
    }

    [Test(Description = "A route limited to some methods keeps only those operations of a path")]
    public void The_methods_of_a_route_limit_the_operations()
    {
        var routes = new[] { new GatewayRoute("/api/{version}/orders/{**rest}", ["GET"]) };

        var document = GatewaySwagger.AsSeenThroughGateway(Document("/api/v1/orders"), routes, servers: null);

        document["paths"]!["/api/v1/orders"]!.AsObject().Select(o => o.Key).ShouldBe(["get"]);
    }

    [Test(Description = "The gateway's servers replace the service's, which point past the gateway")]
    public void The_servers_are_the_gateways()
    {
        var routes = new[] { new GatewayRoute("/api/{version}/orders/{**rest}", []) };
        var servers = new JsonArray(new JsonObject { ["url"] = "https://api.example.com" });

        var withServers = GatewaySwagger.AsSeenThroughGateway(Document("/api/v1/orders"), routes, servers);
        var without = GatewaySwagger.AsSeenThroughGateway(Document("/api/v1/orders"), routes, servers: null);

        withServers["servers"]!.ToJsonString().ShouldBe("""[{"url":"https://api.example.com"}]""");
        without.ContainsKey("servers").ShouldBeFalse();
    }

    [Test(Description = "The swagger route and a route that rewrites the path are not checked against")]
    public void The_routes_of_a_cluster_leave_out_its_swagger_route_and_rewriting_routes()
    {
        var reverseProxy = ReverseProxy(new Dictionary<string, string?>
        {
            ["Routes:orders:ClusterId"] = "orders",
            ["Routes:orders:Match:Path"] = "/api/{version}/orders/{**rest}",
            ["Routes:orders:Match:Methods:0"] = "GET",
            ["Routes:orders-legacy:ClusterId"] = "orders",
            ["Routes:orders-legacy:Match:Path"] = "/legacy/{**rest}",
            ["Routes:orders-legacy:Transforms:0:PathRemovePrefix"] = "/legacy",
            ["Routes:orders-swagger:ClusterId"] = "orders",
            ["Routes:orders-swagger:Match:Path"] = "/swagger/orders/{**rest}",
            ["Routes:billing:ClusterId"] = "billing",
            ["Routes:billing:Match:Path"] = "/api/{version}/billing/{**rest}",
        });

        var route = GatewaySwagger.RoutesOf(reverseProxy, "orders").ShouldHaveSingleItem();
        route.Path.ShouldBe("/api/{version}/orders/{**rest}");
        route.Methods.ShouldBe(["GET"]);
    }
}
