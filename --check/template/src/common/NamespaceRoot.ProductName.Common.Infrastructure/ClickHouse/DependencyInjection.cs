using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Contracts.Health;

namespace NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;

public static class DependencyInjection
{
    /// <summary>
    /// Registers ClickHouse from the <c>ClickHouse</c> section: connections, the schema guard and, while
    /// it is switched on, a readiness check. Readers and writers stay in the service, behind its own ports.
    /// </summary>
    public static IServiceCollection AddClickHouse(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ClickHouseOptions>()
            .BindConfiguration(ClickHouseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One HTTP client for every connection, so sockets are pooled. ClickHouse compresses its
        // responses, so the handler has to decompress them.
        services.AddHttpClient(ClickHouseConnections.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            });

        services.AddSingleton<IClickHouseConnections, ClickHouseConnections>();
        services.AddSingleton<ClickHouseSchemaGuard>();

        if (configuration.GetValue($"{ClickHouseOptions.SectionName}:Enabled", defaultValue: true))
            services.AddHealthChecks().AddCheck<ClickHouseHealthCheck>("clickhouse", tags: [HealthConstants.ReadyTag]);

        return services;
    }
}
