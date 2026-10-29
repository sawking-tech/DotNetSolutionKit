using Hangfire;
//#if (Database != "mssql")
using Hangfire.PostgreSql;
//#endif
//#if (Database == "mssql")
using Hangfire.SqlServer;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.SqlServer;
//#endif
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure;

// Hangfire, with its storage in the service's own schema of the solution's database. Hangfire keeps a
// storage package per database, so this part of the wiring is the one place of the service, beside
// DatabaseProvider, that knows which database it is.
public static partial class DependencyInjection
{
//#if (Database == "mssql")
    // "Hangfire" in ASCII: one key for every service of the database.
    private const long HangfireInstallLockKey = 0x48616E6766697265;

//#endif
    /// <summary>
    /// Register Hangfire and background job services.
    /// </summary>
    private static IServiceCollection AddBackgroundJobs(this IServiceCollection services, string connectionString)
    {
        var serviceName = typeof(DomainMarker).Namespace!;
        var hangfireSchemaName = $"{ServiceIdentifierDbContext.DefaultSchemaName}_hangfire";

        // Guard for Hangfire Schema
        DatabaseProvider.EnsureExclusiveSchema(connectionString, hangfireSchemaName, serviceName);
//#if (Database == "mssql")

        // Hangfire.SqlServer installs its tables in a transaction that deadlocks with another service
        // installing into the same database at the same moment, and gives up after three attempts, leaving
        // the service without a job server. The installs take turns under one lock for the whole database.
        var installLock = new SqlServerMigrationLock();
        using (var connection = installLock.Connect(connectionString))
        {
            installLock.Acquire(connection, HangfireInstallLockKey);
            SqlServerObjectsInstaller.Install(connection, hangfireSchemaName);
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
                SchemaName = hangfireSchemaName,
                PrepareSchemaIfNecessary = true
            });
//#endif
//#if (Database == "mssql")
            config.UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = hangfireSchemaName,
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
        services.AddDomainEventJobActivator();
        services.AddHostedService(sp => new HangfireFilterInstaller(sp));

        return services;
    }
}
