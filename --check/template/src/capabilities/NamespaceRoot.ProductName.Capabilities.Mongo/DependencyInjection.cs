using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NamespaceRoot.ProductName.Common.Contracts.Health;

namespace NamespaceRoot.ProductName.Capabilities.Mongo;

public static class DependencyInjection
{
    /// <summary>
    /// Registers MongoDB from the <c>MongoDB</c> section: one client for the process, the service's
    /// database and, while it is switched on, a readiness check.
    /// </summary>
    public static IServiceCollection AddMongoDB(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoOptions>()
            .BindConfiguration(MongoOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // A Guid is stored as the standard UUID subtype. The driver has no default and refuses to write
        // a Guid without one; the legacy subtype differs between drivers of other languages.
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

        // One client for the process: it owns the connection pool, and the container disposes it. It is
        // built on the first request for the database, so a service with MongoDB switched off has none.
        services.AddSingleton<IMongoClient>(sp =>
            new MongoClient(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString));
        services.AddSingleton<IMongoStore, MongoStore>();

        if (configuration.GetValue($"{MongoOptions.SectionName}:Enabled", defaultValue: true))
            // A server that is down would hold the ping for the driver's 30 seconds of server selection; the
            // probe gives up long before and learns nothing of what failed.
            services.AddHealthChecks().AddCheck<MongoHealthCheck>(
                "mongo", tags: [HealthConstants.ReadyTag], timeout: TimeSpan.FromSeconds(5));

        return services;
    }
}
