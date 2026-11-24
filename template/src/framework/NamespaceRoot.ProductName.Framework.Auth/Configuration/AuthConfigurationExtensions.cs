using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Application.Configuration;

namespace NamespaceRoot.ProductName.Framework.Auth.Configuration;

public static class AuthConfigurationExtensions
{
    private static IServiceCollection AddConfiguration<TConfig, TInterface>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TConfig : class, TInterface, new()
        where TInterface : class
    {
        services.AddOptions<TConfig>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.Configure<TConfig>(configuration.GetSection(sectionName));
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

    public static IServiceCollection AddAuthValidationConfiguration(this IServiceCollection services, IConfiguration configuration) =>
        services.AddConfiguration<AuthConfiguration, IAuthConfiguration>(configuration, AuthConfiguration.SectionName);
}
