using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NamespaceRoot.ProductName.Common.Contracts.Health;
using NamespaceRoot.ProductName.Common.Web.Health;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Health;

/// <summary>
/// What a probe sees: /health answers without touching dependencies, /ready reports each one by name
/// and fails when any of them does.
/// </summary>
[TestFixture]
internal class HealthEndpointsTests
{
    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(HealthStatus database)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHealthChecks()
            .AddCheck("database", () => new HealthCheckResult(database), tags: [HealthConstants.ReadyTag])
            .AddCheck("not-for-readiness", () => HealthCheckResult.Unhealthy());

        var app = builder.Build();
        app.MapPlatformHealth();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    [Test(Description = "Liveness runs no check, so a failing dependency does not make the process look dead")]
    public async Task Health_Should_Return200_When_ADependencyIsDown()
    {
        var (app, client) = await StartAsync(HealthStatus.Unhealthy);
        await using var host = app;

        var response = await client.GetAsync(HealthConstants.Health);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().ShouldBe("Healthy");
        body.GetProperty("checks").EnumerateObject().ShouldBeEmpty();
        body.TryGetProperty("commit", out _).ShouldBeTrue();
        body.TryGetProperty("version", out _).ShouldBeTrue();
    }

    [Test(Description = "Readiness runs only the checks tagged ready, and reports each by name")]
    public async Task Ready_Should_Return200_And_NameTheChecks_When_DependenciesAreUp()
    {
        var (app, client) = await StartAsync(HealthStatus.Healthy);
        await using var host = app;

        var response = await client.GetAsync(HealthConstants.Ready);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var checks = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("checks");
        checks.GetProperty("database").GetString().ShouldBe("Healthy");
        checks.TryGetProperty("not-for-readiness", out _).ShouldBeFalse();
    }

    [Test(Description = "Readiness answers 503 when a dependency is down, so traffic is held back")]
    public async Task Ready_Should_Return503_When_ADependencyIsDown()
    {
        var (app, client) = await StartAsync(HealthStatus.Unhealthy);
        await using var host = app;

        var response = await client.GetAsync(HealthConstants.Ready);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("status").GetString().ShouldBe("Unhealthy");
        body.GetProperty("checks").GetProperty("database").GetString().ShouldBe("Unhealthy");
    }
}
