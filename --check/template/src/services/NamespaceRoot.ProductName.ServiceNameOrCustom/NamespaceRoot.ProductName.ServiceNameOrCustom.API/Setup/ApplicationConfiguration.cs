using NamespaceRoot.ProductName.Common.Application.Configuration;
//#if (FeatureFlags)
using NamespaceRoot.ProductName.Common.Application.FeatureManagement;
//#endif
//#if (SecretStore)
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;
//#endif
//#if (DiffApi)
using NamespaceRoot.ProductName.Common.Web.Setup;
//#endif
namespace NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;

internal static class ApplicationConfiguration
{
    public static IConfigurationBuilder SetupAppConfiguration(
        this IConfigurationBuilder builder, IHostEnvironment env, string[] args)
    {
//#if (FeatureFlags)
        // Platform feature flags, shipped from Common so every service reads the same file and a
        // feature means one thing across the platform. First, so everything below can override it.
        builder.AddPlatformFeatures();

//#endif
        // Always base config
        builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        // Environment-specific configuration
        if (!string.IsNullOrWhiteSpace(env.EnvironmentName))
        {
            var envConfigFile = $"appsettings.{env.EnvironmentName}.json";
            builder.AddJsonFile(envConfigFile, optional: true, reloadOnChange: true);
        }

        // Secret files only for Local
        if (env.IsEnvironment("Local"))
        {
            builder.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true);
        }

        // Always connect environment variables (secrets, dynamic parameters)
        builder.AddEnvironmentVariables();

//#if (Infisical)
        // The secret store goes last, so a value it holds wins over anything shipped in the image. Its
        // connection (project, environment, folders, whether it is required) is read from the Infisical
        // section of configuration; the machine identity comes from environment variables, added above.
        // The arguments are fallbacks for when configuration says nothing: the service's own folder, and
        // a store that may be missing only on a developer machine.
//#if (DiffApi)
        // Skipped for --schema-only: a build agent producing the API document holds no store identity.
        if (!SchemaOnlyMode.IsEnabled(args))
        {
            builder.AddPlatformSecrets("/servicenameorcustom", optional: env.IsEnvironment("Local"));
        }
//#else
        builder.AddPlatformSecrets("/servicenameorcustom", optional: env.IsEnvironment("Local"));
//#endif

//#endif
//#if (Vault)
        // The same for Vault: the shared secret, then this service's, from the Vault section of
        // configuration; the token or the AppRole comes from environment variables, added above.
//#if (DiffApi)
        if (!SchemaOnlyMode.IsEnabled(args))
        {
            builder.AddPlatformVaultSecrets("servicenameorcustom", optional: env.IsEnvironment("Local"));
        }
//#else
        builder.AddPlatformVaultSecrets("servicenameorcustom", optional: env.IsEnvironment("Local"));
//#endif

//#endif
        // With the database switched off, what is stored in it goes off too: the job server, and the bus
        // when it delivers through the outbox.
//#if (Messaging == "outbox")
        builder.AddDependencyOverrides(busNeedsDatabase: true);
//#else
        builder.AddDependencyOverrides(busNeedsDatabase: false);
//#endif

//#if (DiffApi)
        // A schema-only run turns every infrastructure switch off, last so it wins over the files and
        // the environment.
        builder.AddSchemaOnlyOverrides(args);

//#endif
        return builder;
    }
}
