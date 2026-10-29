using Hangfire;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;
using NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.Security.Filters;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;

/// <summary>
/// Extensions for configuring Hangfire background jobs and dashboard.
/// </summary>
internal static class BackgroundJobsSetup
{
    /// <summary>
    /// Configures Hangfire dashboard with administrative authorization.
    /// </summary>
    /// <param name="app">Application builder.</param>
    /// <returns>Updated application builder.</returns>
    internal static WebApplication UseAppHangfire(this WebApplication app)
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
