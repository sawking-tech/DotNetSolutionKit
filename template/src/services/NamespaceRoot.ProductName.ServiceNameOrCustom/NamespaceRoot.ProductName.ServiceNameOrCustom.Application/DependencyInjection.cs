using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Application;

/// <summary>
/// Extensions for registering application services in DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Register application services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Updated service collection.</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddApplicationConfiguration();

        // Domain services (policies and the like) are registered here rather than by the API,
        // which works with the domain only through this layer.
        services.AddDomainServices();

        // Register services

        return services;
    }

    /// <summary>
    /// Register and validate application configuration settings.
    /// </summary>
    /// <param name="services">Service collection.</param>
    private static void AddApplicationConfiguration(this IServiceCollection services)
    {
        // Add configuration settings
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
