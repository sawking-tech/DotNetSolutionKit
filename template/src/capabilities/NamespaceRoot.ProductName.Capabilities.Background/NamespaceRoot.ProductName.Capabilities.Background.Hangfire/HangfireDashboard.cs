using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace NamespaceRoot.ProductName.Capabilities.Background.Hangfire;

/// <summary>
/// Extensions for configuring Hangfire background jobs and dashboard.
/// </summary>
public static class HangfireDashboard
{
    /// <summary>
    /// Configures Hangfire dashboard with administrative authorization.
    /// </summary>
    /// <param name="app">Application builder.</param>
    /// <returns>Updated application builder.</returns>
    public static WebApplication UseAppHangfire(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<IHangfireSettings>();

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new HangfireAdminAuthorizationFilter(settings)],
            DashboardTitle = "NamespaceRoot ServiceNameOrCustom Jobs",
            AppPath = "/swagger"
        });

        return app;
    }
}
