using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace NamespaceRoot.ProductName.Common.Application.Configuration;

public static class AuthConfigurationExtensions
{
    private static IServiceCollection AddConfiguration<TConfig, TInterface>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TConfig : class, TInterface, new()
        where TInterface : class
    {
        // Register with validation
        services.AddOptions<TConfig>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Register for IOptions<T> pattern
        services.Configure<TConfig>(configuration.GetSection(sectionName));

        // Register strongly-typed singleton with interface. TryAdd: a gateway and the shared JWT setup
        // can both ask for the same settings, and a second registration would only shadow the first.
        services.TryAddSingleton<TInterface>(provider =>
            provider.GetRequiredService<IOptions<TConfig>>().Value);

        return services;
    }

    /// <summary>Registers JWT public configuration (validation-only services: Gateway, Core, Billing).</summary>
    public static IServiceCollection AddJwtPublicConfiguration(this IServiceCollection services, IConfiguration configuration) =>
        services.AddConfiguration<JwtPublicConfiguration, IJwtPublicConfiguration>(configuration, JwtPublicConfiguration.SectionName);

    /// <summary>Registers full JWT configuration (Auth service — signs and validates tokens).</summary>
    public static IServiceCollection AddJwtConfiguration(this IServiceCollection services, IConfiguration configuration) =>
        services.AddConfiguration<JwtConfiguration, IJwtConfiguration>(configuration, JwtConfiguration.SectionName);

    public static IServiceCollection AddRefreshTokenConfiguration(this IServiceCollection services, IConfiguration configuration) =>
        services.AddConfiguration<RefreshTokenConfiguration, IRefreshTokenConfiguration>(configuration, RefreshTokenConfiguration.SectionName);

    public static IServiceCollection AddInternalApiConfiguration(this IServiceCollection services, IConfiguration configuration) =>
        services.AddConfiguration<InternalApiConfiguration, IInternalApiConfiguration>(configuration, InternalApiConfiguration.SectionName);

    public static IServiceCollection AddAuthValidationConfiguration(this IServiceCollection services, IConfiguration configuration) =>
        services.AddConfiguration<AuthConfiguration, IAuthConfiguration>(configuration, AuthConfiguration.SectionName);
}
