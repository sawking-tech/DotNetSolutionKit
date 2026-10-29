using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Common.Contracts.Health;
using Serilog;
using Serilog.Events;

namespace NamespaceRoot.ProductName.Common.Web.Setup;

/// <summary>
/// Serilog for a service host: the same enrichment, filtering and request log line in every service.
/// </summary>
public static class PlatformLogging
{
    /// <summary>
    /// Configures Serilog from the <c>Serilog</c> section of configuration, before the host is built.
    /// </summary>
    public static WebApplicationBuilder AddPlatformLogging(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("version", assembly.GetName().Version?.ToString() ?? "unknown")
            .Enrich.WithProperty("module", configuration["Application:Name"] ?? "App")
            .MinimumLevel.Information()
            .Filter.ByExcluding(IsHealthCheckRequest)
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        builder.Host.UseSerilog();

        return builder;
    }

    /// <summary>
    /// Logs the start and stop of the application and one line per request.
    /// </summary>
    public static WebApplication UsePlatformRequestLogging(this WebApplication app)
    {
        var assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "App";

        app.Logger.LogInformation("The {EntryAssemblyName} application started", assemblyName);

        app.Lifetime.ApplicationStopped.Register(() =>
        {
            app.Logger.LogInformation("The {EntryAssemblyName} application was stopped", assemblyName);
            Log.CloseAndFlush();
        });

        app.UseSerilogRequestLogging(options =>
        {
            // Probes run every few seconds; at Information they would drown the requests.
            options.GetLevel = (context, _, _) =>
                HealthConstants.AllPaths.Contains(context.Request.Path.Value)
                    ? LogEventLevel.Verbose
                    : LogEventLevel.Information;

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);

                if (httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgent))
                {
                    diagnosticContext.Set("UserAgent", userAgent.ToString());
                }
            };
        });

        return app;
    }

    private static bool IsHealthCheckRequest(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue("RequestPath", out var propertyValue))
        {
            return false;
        }

        var path = propertyValue.ToString().Trim('"');
        return HealthConstants.AllPaths.Contains(path) && logEvent.Level <= LogEventLevel.Information;
    }
}
