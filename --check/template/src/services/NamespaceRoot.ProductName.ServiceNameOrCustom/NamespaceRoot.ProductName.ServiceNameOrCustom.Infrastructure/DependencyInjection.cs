using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Domain.Persistence;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;
//#if (Messaging != "none")
using NamespaceRoot.ProductName.Common.Infrastructure.Messaging;
//#endif
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;
//#if (AuditEnabled)
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;
//#endif
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence;
//#if (ClickHouse)
using NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;
//#endif
//#if (MongoDB)
using NamespaceRoot.ProductName.Capabilities.Mongo;
//#endif
//#if (NotifyEmail)
using NamespaceRoot.ProductName.Capabilities.Notifications;
//#endif
//#if (Storage)
using NamespaceRoot.ProductName.Common.Infrastructure.Storage;
//#endif
using NamespaceRoot.ProductName.ServiceNameOrCustom.Application;
using NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework;
using NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework.DataSeeding;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure;

/// <summary>
/// Extensions for registering infrastructure services in DI container.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMethodReturnValue.Local")]
public static partial class DependencyInjection
{
    /// <summary>
    /// Register infrastructure services.
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        // What this run uses: Database, HangfireSettings and RabbitMq each have an Enabled switch.
        var switches = DependencySwitches.Read(configuration);

        // Database
        string connectionString;
        if (switches.Database)
        {
            connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
            if (string.IsNullOrEmpty(connectionString))
                throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' not found. Set ConnectionStrings__DefaultConnection, " +
                    $"or run without a database: {DependencySwitches.DatabaseKey}=false.");

            // Unique name for this microservice (used for schema ownership)
            var serviceName = typeof(DomainMarker).Namespace!;

            // 1. Guard for Main Database Schema
            DatabaseProvider.EnsureExclusiveSchema(connectionString, ServiceIdentifierDbContext.DefaultSchemaName, serviceName);
        }
        else
        {
            // The context stays registered so everything built on it still resolves; opening a
            // connection answers 503 instead.
            connectionString = SwitchedOffDatabase.ConnectionString;
        }

        // Domain events: handlers from the application layer, run in three phases around SaveChanges and
        // the transaction by the interceptors attached to the context below.
        services.AddDomainEvents(typeof(ApplicationMarker).Assembly);

        // AddDbContext, not the pool: interceptors resolved per scope (the domain events', the outbox's)
        // are not re-attached to a context handed back by the pool, and they would quietly do nothing.
        services.AddDbContext<ServiceIdentifierDbContext>((sp, options) =>
        {
            options.UseDatabase(connectionString, ServiceIdentifierDbContext.DefaultSchemaName);
            if (!switches.Database)
                options.UseSwitchedOffDatabase();
            options.ApplyDomainEventInterceptors(sp);
//#if (AuditEnabled)
            options.ApplyAuditInterceptor(sp);
//#endif
        });

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ServiceIdentifierDbContext>());

//#if (Messaging == "outbox")
        // The bus, with the outbox in this service's schema: a message and the change that caused it
        // commit together. Consumers are found in this assembly.
        services.AddMessaging<ServiceIdentifierDbContext>(
            configuration, "servicenameorcustom", typeof(InfrastructureMarker).Assembly);
//#if (AuditEnabled)

        // The audit journal: a change to an entity marked [Auditable] publishes AuditRecordedV1 into the
        // outbox in the same save, so a rolled back change leaves no entry. IAuditRecorder and
        // ISetBasedAuditCapture record what bypasses the change tracker; see docs/features/audit.md.
        services.AddAuditPersistence();
        services.AddAuditRecorder<ServiceIdentifierDbContext>("servicenameorcustom");
//#endif

//#elif (Messaging == "direct")
        // The bus without an outbox: a message goes straight to RabbitMQ and is lost if the broker is
        // down. Consumers are found in this assembly.
        services.AddMessaging(configuration, "servicenameorcustom", typeof(InfrastructureMarker).Assembly);

//#endif
        // Repositories

        // Specifications: case-insensitive search; readable numbers from a sequence, taken before the
        // entity is created
        services.AddDatabaseQueries<ServiceIdentifierDbContext>();

//#if (ClickHouse)
        // ClickHouse, the ClickHouse section: connections, the schema check, readiness
        services.AddClickHouse(configuration);

//#endif
//#if (MongoDB)
        // MongoDB, the MongoDB section: the client, the service's database, readiness
        services.AddMongoDB(configuration);

//#endif
//#if (NotifyEmail)
        // Email, the Email section: the transport of the provider it names, the sandbox outside Production
        services.AddNotifications(configuration);

//#endif
//#if (Storage)
        // Object storage, the S3 section; S3:Enabled=false keeps nothing
        services.AddS3ObjectStorage(configuration);

//#endif
        // Configurations
        services.AddInfrastructureConfiguration();

//#if (Hangfire)
        // Background jobs, with their settings validated only when the job server runs. Off together
        // with the database: the database is their storage.
        if (switches.Jobs)
        {
            services.AddValidatedOptions<IHangfireSettings, HangfireSettings>(HangfireSettings.SectionName);
            services.AddBackgroundJobs(connectionString);
        }

//#endif
        // Data Seeding
        services.AddScoped<DataSeeder>();

        return services;
    }

    /// <summary>
    /// Register and validate infrastructure configuration settings.
    /// </summary>
    /// <param name="services">Service collection.</param>
    private static void AddInfrastructureConfiguration(this IServiceCollection services)
    {
        services.AddValidatedOptions<ICorsSettings, CorsSettings>(CorsSettings.SectionName);
    }

    /// <summary>
    /// Helper method to register validated options with interface.
    /// </summary>
    private static IServiceCollection AddValidatedOptions<TInterface, TSettings>(
        this IServiceCollection services,
        string sectionName)
        where TInterface : class
        where TSettings : class, TInterface, new()
    {
        services.AddOptions<TSettings>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TInterface>(sp =>
            sp.GetRequiredService<IOptions<TSettings>>().Value);

        return services;
    }
}
