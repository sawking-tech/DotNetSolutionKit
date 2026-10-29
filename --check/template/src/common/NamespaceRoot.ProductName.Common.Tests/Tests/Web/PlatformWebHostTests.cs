using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Web.Setup;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// The web layer a service gets from AddPlatformWebApi and UsePlatformPipeline, end to end.
/// </summary>
[TestFixture]
internal class PlatformWebHostTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private bool _serviceMiddlewareRan;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Cors:AllowedOrigins:0"] = "https://app.example.com";
        builder.AddPlatformLogging();
        builder.AddPlatformWebApi(typeof(PlatformWebHostTests).Assembly);
        builder.Services.AddHealthChecks();

        _app = builder.Build();
        _app.UsePlatformPipeline(typeof(PlatformWebHostTests).Assembly, authenticate: false, beforeEndpoints: app =>
            app.Use((context, next) =>
            {
                _serviceMiddlewareRan = true;
                return next(context);
            }));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Test(Description = "Health endpoints are mapped and the service's own middleware runs before the endpoints")]
    public async Task Should_AnswerHealth_And_RunServiceMiddleware()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _serviceMiddlewareRan.ShouldBeTrue();
    }

    [Test(Description = "An unknown route is a problem carrying the correlation identifier")]
    public async Task Should_AnswerAProblemWithCorrelation_When_NoRouteMatches()
    {
        var response = await _client.GetAsync("/nowhere");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        response.Headers.Contains(TracingHeaders.CorrelationId).ShouldBeTrue();
    }

    [Test(Description = "A configured origin gets CORS headers on a preflight")]
    public async Task Should_AllowAConfiguredOrigin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "https://app.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").Single().ShouldBe("https://app.example.com");
    }
}
