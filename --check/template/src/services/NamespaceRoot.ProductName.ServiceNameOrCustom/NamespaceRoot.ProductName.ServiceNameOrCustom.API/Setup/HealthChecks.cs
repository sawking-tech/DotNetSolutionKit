using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Contracts.Health;
using NamespaceRoot.ProductName.Common.Web.Health;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence;
using NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;

internal static class HealthChecks
{
    /// <summary>
    /// Registers the dependencies <c>/ready</c> checks. Another dependency joins readiness by being
    /// registered here with <see cref="HealthConstants.ReadyTag"/>. A switched-off dependency is not
    /// checked: the service is ready without it.
    /// </summary>
    public static WebApplicationBuilder SetupHealthChecks(this WebApplicationBuilder builder)
    {
        var switches = DependencySwitches.Read(builder.Configuration);
        var checks = builder.Services.AddHealthChecks();

//#if (Hangfire)
        if (switches.Jobs)
            checks.AddHangfire(options => options.MinimumAvailableServers = 1, name: "hangfire", tags: [HealthConstants.ReadyTag]);

//#endif
        if (switches.Database)
            checks.AddDbContextCheck<ServiceIdentifierDbContext>(name: DatabaseProvider.Name, tags: [HealthConstants.ReadyTag]);

        return builder;
    }

    public static WebApplication MapHealthEndpoints(this WebApplication app) => app.MapPlatformHealth();
}
