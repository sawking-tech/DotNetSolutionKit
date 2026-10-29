//#if (FeatureFlags)
using NamespaceRoot.ProductName.Common.Application.FeatureManagement;
//#endif
using NamespaceRoot.ProductName.Common.Application.Configuration;
//#if (DiffApi)
using NamespaceRoot.ProductName.Common.Web.Setup;
//#endif
using NamespaceRoot.ProductName.ServiceNameOrCustom.Application;
using NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;

internal static class ApplicationSetup
{
    public static WebApplicationBuilder SetupAppServices(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var services = builder.Services;

//#if (FeatureFlags)
        // Platform feature flags: the catalogue, the store, and the library that evaluates them.
        // Flags come from the shared features.json, so nothing is declared here: a service gains a
        // new flag without a line of code, and adds a constant to FeatureKeys only for the ones its
        // own code reads.
        services.AddPlatformFeatureManagement();

//#endif
        // services.AddJwtConfiguration(configuration);
        services.AddInternalApiConfiguration(configuration);
        services.AddAuthValidationConfiguration(configuration);

        // Register Application Layer services. They register the domain services too:
        // the API reaches the domain only through the application layer.
        services.AddApplicationServices();

        // Register Infrastructure Layer services
//#if (DiffApi)
        // A schema-only run must not touch infrastructure: it registers no database, no broker and no
        // jobs, and only has to get far enough to describe the API.
        if (!SchemaOnlyMode.IsEnabled())
        {
            services.AddInfrastructureServices(builder.Configuration);
        }
//#else
        services.AddInfrastructureServices(builder.Configuration);
//#endif

        return builder;
    }
}
