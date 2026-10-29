using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;

namespace NamespaceRoot.ProductName.Common.Web.Setup;

/// <summary>
/// One CORS policy for a service host, read from the <c>Cors</c> section of configuration.
/// </summary>
public static class PlatformCors
{
    public const string PolicyName = "PlatformCors";

    public static WebApplicationBuilder AddPlatformCors(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>();

        builder.Services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            policy.SetIsOriginAllowed(origin => IsOriginAllowed(origin, settings?.AllowedOrigins));

            if (settings?.AllowedMethods.Length > 0)
                policy.WithMethods(settings.AllowedMethods);
            else
                policy.AllowAnyMethod();

            if (settings?.AllowedHeaders.Length > 0)
                policy.WithHeaders(settings.AllowedHeaders);
            else
                policy.AllowAnyHeader();

            if (settings?.AllowCredentials == true)
                policy.AllowCredentials();

            if (settings?.ExposedHeaders.Length > 0)
                policy.WithExposedHeaders(settings.ExposedHeaders);

            if (settings?.PreflightMaxAge > 0)
                policy.SetPreflightMaxAge(TimeSpan.FromSeconds(settings.PreflightMaxAge));
        }));

        return builder;
    }

    public static WebApplication UsePlatformCors(this WebApplication app)
    {
        app.UseCors(PolicyName);
        return app;
    }

    /// <summary>
    /// Whether <paramref name="origin"/> matches one of the allowed patterns: <c>*</c> for any origin, an
    /// exact origin, or <c>scheme://host:*</c> for any port of a host. Compared without regard to case.
    /// </summary>
    internal static bool IsOriginAllowed(string origin, string[]? allowedPatterns)
    {
        if (string.IsNullOrEmpty(origin) || allowedPatterns is null) return false;

        foreach (var pattern in allowedPatterns)
        {
            if (pattern == "*") return true;

            // "https://localhost:*" allows every port of that host: the prefix is "https://localhost:".
            if (pattern.EndsWith(":*", StringComparison.Ordinal)
                && origin.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(origin, pattern, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
