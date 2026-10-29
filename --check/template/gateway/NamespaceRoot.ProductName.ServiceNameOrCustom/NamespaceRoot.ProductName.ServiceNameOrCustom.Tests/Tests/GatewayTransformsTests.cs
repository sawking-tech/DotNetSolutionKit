using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Web.Setup;
using NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Tests.Tests;

/// <summary>
/// What the gateway sends to a service: the request as YARP forwards it with the gateway's transforms,
/// caught before it leaves the gateway.
/// </summary>
public class GatewayTransformsTests
{
    private const string Site = "https://app.example.com";

    private static async Task<(HttpRequestMessage Sent, HttpResponseMessage Answer)> Forward(
        HttpRequestMessage request, params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value));
        builder.AddPlatformCors();
        builder.Services.AddInternalApiConfiguration(builder.Configuration);
        builder.Services.AddReverseProxy()
            .LoadFromMemory(
                [new RouteConfig { RouteId = "orders", ClusterId = "orders", Match = new RouteMatch { Path = "/api/{**rest}" } }],
                [new ClusterConfig
                {
                    ClusterId = "orders",
                    Destinations = new Dictionary<string, DestinationConfig> { ["primary"] = new() { Address = "http://orders:8080/" } },
                }])
            .AddGatewayTransforms();
        var service = new CapturingService();
        builder.Services.AddSingleton<IForwarderHttpClientFactory>(service);

        await using var app = builder.Build();
        app.UsePlatformCors();
        app.MapReverseProxy();
        await app.StartAsync();
        var answer = await app.GetTestClient().SendAsync(request);
        return (service.Received ?? throw new InvalidOperationException("the gateway sent nothing to the service"), answer);
    }

    private static HttpRequestMessage FromSite()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/orders");
        request.Headers.Add("Origin", Site);
        return request;
    }

    private static string[] Cors(HttpResponseMessage answer) =>
        answer.Headers.Where(h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}").OrderBy(h => h).ToArray();

    [Test]
    public async Task The_service_does_not_see_the_origin()
    {
        var request = FromSite();
        request.Headers.Add("Accept-Language", "de");

        var (sent, _) = await Forward(request);

        sent.Headers.Contains("Origin").ShouldBeFalse(
            "a service that sees the Origin adds its own CORS headers, which pass over the gateway's");
        sent.Headers.AcceptLanguage.ToString().ShouldBe("de");
    }

    [Test]
    public async Task A_service_s_cors_headers_do_not_reach_the_client() =>
        Cors((await Forward(FromSite())).Answer).ShouldBeEmpty(
            "the gateway allows no origin, so the service's own CORS headers would let the site read the answer");

    [Test]
    public async Task The_gateway_s_cors_headers_reach_the_client() =>
        Cors((await Forward(FromSite(), ("Cors:AllowedOrigins:0", Site), ("Cors:AllowCredentials", "true"))).Answer)
            .ShouldBe([$"Access-Control-Allow-Credentials: true", $"Access-Control-Allow-Origin: {Site}"]);

    /// <summary>
    /// The service behind the gateway: records the request and answers 200 with CORS headers of its own, as a
    /// service that does not look at the Origin would.
    /// </summary>
    private sealed class CapturingService : IForwarderHttpClientFactory
    {
        public HttpRequestMessage? Received { get; private set; }

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(new Handler(this));

        private sealed class Handler(CapturingService service) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                service.Received = request;
                var answer = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                answer.Headers.Add("Access-Control-Allow-Origin", Site);
                answer.Headers.Add("Access-Control-Allow-Credentials", "true");
                return Task.FromResult(answer);
            }
        }
    }
}
