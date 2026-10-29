// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NamespaceRoot.ProductName.Common.Application.Authorization;
using NamespaceRoot.ProductName.Common.Application.Serialization;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;
using NamespaceRoot.ProductName.Common.Web.Authentication;
using NamespaceRoot.ProductName.Common.Web.Authorization;
using NamespaceRoot.ProductName.Common.Web.Errors;
using NamespaceRoot.ProductName.Common.Web.Health;
using NamespaceRoot.ProductName.Common.Web.Swagger;

namespace NamespaceRoot.ProductName.Common.Web.Setup;

/// <summary>
/// The web layer every service host shares: what is registered and the order of the pipeline.
/// </summary>
/// <remarks>
/// Kept here rather than in each service, so a fix to the pipeline reaches every service with a
/// Common update instead of a change copied into each of them. A service adds only what is its own:
/// its configuration, its registrations, its health checks and its authentication handler.
/// </remarks>
public static class PlatformWebHost
{
    /// <summary>
    /// Registers JSON, error handling, controllers with the permission check, validation, Swagger, CORS
    /// and the execution context.
    /// </summary>
    /// <param name="builder">The service's builder.</param>
    /// <param name="serviceAssembly">The service's API assembly: its validators, controllers' versions
    /// and XML comments are read from it.</param>
    /// <param name="configureMvc">The service's own MVC additions: filters, application parts.</param>
    public static WebApplicationBuilder AddPlatformWebApi(
        this WebApplicationBuilder builder,
        Assembly serviceAssembly,
        Action<IMvcBuilder>? configureMvc = null)
    {
        builder.Services.ConfigurePlatformJson();
        builder.Services.AddPlatformForwardedHeaders(builder.Configuration);

        // Errors are RFC 9457 problems with a correlation identifier; see Common.Web/Errors.
        builder.Services.AddPlatformErrorHandling(builder.Environment);

        // Every action marked [RequiredPermissions] is checked; the permissions come from the token, or
        // from a separate service with Permissions:Source = remote, unless the service registers its own
        // IPermissionService.
        var mvc = builder.Services.AddControllers(options => options.Filters.Add<PermissionAuthorizationFilter>());
        configureMvc?.Invoke(mvc);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddPlatformPermissions(builder.Configuration);

        builder.Services.AddValidation(serviceAssembly);
        builder.SetupSwaggerPage(serviceAssembly);
        builder.AddPlatformCors();

        // How the token cookies are sent is the deployment's to state; a combination the browser or the
        // CSRF check cannot honour stops the start here, with what to change.
        var authCookies = builder.Configuration.GetSection(AuthCookieSettings.SectionName).Get<AuthCookieSettings>()
                          ?? new AuthCookieSettings();
        authCookies.Validate(builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>());
        builder.Services.AddSingleton(authCookies);

        // /health and /ready are mapped by UsePlatformPipeline; a host with no dependency to check
        // (a gateway) still needs the health check services behind them.
        builder.Services.AddHealthChecks();

        builder.Services.AddMemoryCache();
        builder.Services.AddExecutionContext();
        builder.Services.AddSingleton(TimeProvider.System);

        return builder;
    }

    /// <summary>
    /// Builds the pipeline in the order the platform relies on.
    /// </summary>
    /// <param name="app">The built application.</param>
    /// <param name="serviceAssembly">The service's API assembly, for the Swagger page.</param>
    /// <param name="authenticate">Whether to add authentication and authorization; false only where
    /// the schemes were not registered, as in a schema-only run.</param>
    /// <param name="beforeEndpoints">The service's own middleware, after authorization and before
    /// the endpoints: a dashboard, for instance.</param>
    /// <remarks>
    /// Needs <see cref="PlatformLogging.AddPlatformLogging"/> on the builder: the request log line comes
    /// from Serilog. Tracing goes first, so the request log line carries the correlation identifier. Error handling
    /// goes after CORS, so an error reaches a browser with CORS headers, and before authentication, so
    /// failures there are answered as problems too.
    /// </remarks>
    public static WebApplication UsePlatformPipeline(
        this WebApplication app,
        Assembly serviceAssembly,
        bool authenticate = true,
        Action<WebApplication>? beforeEndpoints = null)
    {
        // First, so everything after sees the client's scheme and address, not the proxy's.
        app.UsePlatformForwardedHeaders();
        app.UsePlatformTracing();
        app.UsePlatformRequestLogging();
        app.UseRouting();
        app.UsePlatformCors();
        app.UsePlatformErrorHandling();

        // A state-changing request a token cookie would authenticate needs the CSRF header. After error
        // handling, so the refusal is a problem the frontend can read; before authentication, which would
        // otherwise accept the cookie.
        app.UseMiddleware<CsrfProtectionMiddleware>();

        if (authenticate)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        app.UseSwaggerPage(serviceAssembly);
        beforeEndpoints?.Invoke(app);

        app.MapControllers();
        app.MapPlatformHealth();

        return app;
    }

    /// <summary>
    /// <see cref="PlatformJson"/> for controllers and for anything written with <c>WriteAsJsonAsync</c>.
    /// </summary>
    public static IServiceCollection ConfigurePlatformJson(this IServiceCollection services)
    {
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => PlatformJson.Apply(options.JsonSerializerOptions));
        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => PlatformJson.Apply(options.SerializerOptions));
        return services;
    }
}
