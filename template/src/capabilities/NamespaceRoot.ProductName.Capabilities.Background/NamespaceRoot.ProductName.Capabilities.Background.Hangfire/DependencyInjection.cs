using Hangfire;
//#if (Database != "mssql")
using Hangfire.PostgreSql;
//#endif
//#if (Database == "mssql")
using Hangfire.SqlServer;
using NamespaceRoot.ProductName.Capabilities.Persistence.Mssql;
//#endif
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence;

namespace NamespaceRoot.ProductName.Capabilities.Background.Hangfire;

// Hangfire, with its storage in the service's own schema of the solution's database. Hangfire keeps a
// storage package per database, so this wiring and the database provider switch in Common are the two
// places that know which database the solution uses.
public static class DependencyInjection
{
//#if (Database == "mssql")
    // "Hangfire" in ASCII: one key for every service of the database.
    private const long HangfireInstallLockKey = 0x48616E6766697265;

//#endif
    /// <summary>Register Hangfire, its storage in the service's schema, and the job filters.</summary>
    /// <param name="serviceName">The service that owns the schema, named in the guard's message.</param>
    /// <param name="schemaName">The schema of the job storage, the service's own name and a suffix.</param>
    public static IServiceCollection AddHangfireJobs(
        this IServiceCollection services,
        string connectionString,
        string serviceName,
        string schemaName)
    {
        // Guard for Hangfire Schema
        DatabaseProvider.EnsureExclusiveSchema(connectionString, schemaName, serviceName);
//#if (Database == "mssql")

        // Hangfire.SqlServer installs its tables in a transaction that deadlocks with another service
        // installing into the same database at the same moment, and gives up after three attempts, leaving
        // the service without a job server. The installs take turns under one lock for the whole database.
        var installLock = new SqlServerMigrationLock();
        using (var connection = installLock.Connect(connectionString))
        {
            installLock.Acquire(connection, HangfireInstallLockKey);
            SqlServerObjectsInstaller.Install(connection, schemaName);
            installLock.Release(connection, HangfireInstallLockKey);
        }
//#endif

        services.AddHangfire(config =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings();
//#if (Database != "mssql")
            config.UsePostgreSqlStorage(c =>
                c.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions
            {
                SchemaName = schemaName,
                PrepareSchemaIfNecessary = true
            });
//#endif
//#if (Database == "mssql")
            config.UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = schemaName,
                // Installed above, under the lock.
                PrepareSchemaIfNecessary = false
            });
//#endif
        });

        services.AddHangfireServer((sp, options) =>
        {
            var settings = sp.GetRequiredService<IHangfireSettings>();
            options.WorkerCount = settings.WorkerCount;
        });

        // Jobs run in a scope the domain event interceptors can see, so events a job raises are not
        // dropped; the filter carries the person who enqueued a job into it, for attribution.
        services.AddSingleton<JobActivator>(sp =>
            new DomainEventJobActivator(sp.GetRequiredService<IServiceScopeFactory>()));
        services.AddSingleton<JobActorPropagationFilter>();
        services.AddHostedService(sp => new HangfireFilterInstaller(sp));

        return services;
    }
}
