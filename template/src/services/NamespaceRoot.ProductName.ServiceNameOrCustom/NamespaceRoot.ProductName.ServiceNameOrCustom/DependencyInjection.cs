using Microsoft.Extensions.DependencyInjection;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom;

/// <summary>
/// Extensions for registering domain services in DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Register domain services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Updated service collection.</returns>
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        // Register Domain services

        return services;
    }
}
