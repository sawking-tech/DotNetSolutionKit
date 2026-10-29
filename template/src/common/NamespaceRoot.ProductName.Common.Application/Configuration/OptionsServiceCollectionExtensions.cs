using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NamespaceRoot.ProductName.Common.Application.Configuration;

public static class OptionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers validated options bound to a configuration section,
    /// with the interface resolved as a singleton from the DI container.
    /// Validation runs on application startup via DataAnnotations.
    /// </summary>
    public static IServiceCollection AddValidatedOptions<TInterface, TSettings>(
        this IServiceCollection services,
        string sectionName)
        where TInterface : class
        where TSettings : class, TInterface, new()
    {
        services.ValidateOptions<TSettings>(sectionName);

        services.AddSingleton<TInterface>(sp =>
            sp.GetRequiredService<IOptions<TSettings>>().Value);

        return services;
    }

    /// <summary>
    /// Registers a setting that changes while the service runs, as <see cref="IReloadable{T}"/>: validated at
    /// startup like any other, and afterwards replaced by each new valid value of its section.
    /// </summary>
    /// <remarks>
    /// For what is read on each use - a limit, a list of allowed origins, a message - and not for what is
    /// built once from a value, which keeps the old one until a restart whatever the setting says.
    /// </remarks>
    public static IServiceCollection AddReloadableOptions<TSettings>(
        this IServiceCollection services,
        string sectionName)
        where TSettings : class, new()
    {
        services.ValidateOptions<TSettings>(sectionName);
        services.AddSingleton<IReloadable<TSettings>>(sp => new Reloadable<TSettings>(
            sp.GetRequiredService<IConfiguration>().GetSection(sectionName),
            sp.GetRequiredService<ILogger<Reloadable<TSettings>>>()));
        return services;
    }

    /// <summary>
    /// Binds options to a configuration section and validates via DataAnnotations on startup.
    /// Does not register the interface in DI.
    /// </summary>
    public static void ValidateOptions<TSettings>(
        this IServiceCollection services,
        string sectionName)
        where TSettings : class, new()
    {
        services.AddOptions<TSettings>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
