using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NamespaceRoot.ProductName.Common.Application.Serialization;
using NamespaceRoot.ProductName.Common.Contracts.Health;

namespace NamespaceRoot.ProductName.Common.Web.Health;

/// <summary>
/// The two health endpoints every service exposes, on ASP.NET Core health checks.
/// </summary>
/// <remarks>
/// <c>/health</c> runs no check: it answers while the process can serve requests, which is what a
/// liveness probe or a container healthcheck asks. <c>/ready</c> runs every check tagged
/// <see cref="HealthConstants.ReadyTag"/> - the database, the bus, the job server - and answers 503 when
/// one of them fails, so a load balancer or a deploy script holds traffic back. A service adds a dependency
/// to readiness by registering a health check with that tag; the endpoints do not change.
/// </remarks>
public static class HealthEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = PlatformJson.Options;

    public static WebApplication MapPlatformHealth(this WebApplication app)
    {
        app.MapHealthChecks(HealthConstants.Health, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteAsync,
        });

        app.MapHealthChecks(HealthConstants.Ready, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthConstants.ReadyTag),
            ResponseWriter = WriteAsync,
        });

        return app;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var time = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        var version = VersionInfo.Current;

        var body = new
        {
            status = report.Status.ToString(),
            service = Assembly.GetEntryAssembly()?.GetName().Name,
            version = version.Version,
            commit = VersionInfo.CurrentCommit,
            releaseNotes = version.ReleaseNotes,
            timestamp = time.GetUtcNow(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString()),
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
