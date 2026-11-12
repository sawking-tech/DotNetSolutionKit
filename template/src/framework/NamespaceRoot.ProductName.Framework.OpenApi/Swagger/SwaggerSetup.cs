using System.Xml.XPath;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;
using NamespaceRoot.ProductName.Framework.OpenApi;
//#if (FeatureFlags)
using NamespaceRoot.ProductName.Framework.FeatureFlags;
//#endif
using NamespaceRoot.ProductName.Framework.OpenApi.Swagger.Filters;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Framework.OpenApi.Swagger;

/// <summary>
/// Standard Swagger setup for internal services (Core, Billing, Auth, etc.).
/// For services with custom Swagger requirements (e.g. API Gateway) override as needed.
/// </summary>
public static class SwaggerSetup
{
    /// <summary>
    /// Registers Swagger generation with versioned docs, security definitions, and standard filters.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="serviceAssembly">The service's own assembly, used for version discovery and XML comments.</param>
    /// <param name="configure">Extra SwaggerGen configuration of the caller, applied after the standard filters.</param>
    public static WebApplicationBuilder SetupSwaggerPage(
        this WebApplicationBuilder builder,
        Assembly serviceAssembly,
        Action<SwaggerGenOptions>? configure = null)
    {
        builder.Services.AddEndpointsApiExplorer();

        // Swagger:PublicServers and Swagger:ScrubPatterns, checked at startup.
        builder.Services.AddValidatedOptions<ISwaggerSettings, SwaggerSettings>(SwaggerSettings.SectionName);
        builder.Services.AddSingleton<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerServers>();

        builder.Services.AddSwaggerGen(options =>
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production;
            var appName = builder.Configuration.GetValue<string>("Application:Name") ?? string.Empty;
            var appDescription = builder.Configuration.GetValue<string>("Application:Description") ?? string.Empty;

            options.SwaggerDoc("all", new OpenApiInfo
            {
                Title = $"{appName} : {environment}",
                Version = "all",
                Description = appDescription
            });

            var versions = ApiVersionHelper.DiscoverAllVersions(serviceAssembly);
            foreach (var version in versions)
            {
                options.SwaggerDoc(version, new OpenApiInfo
                {
                    Title = $"{appName} : {environment}",
                    Version = version,
                    Description = appDescription
                });
            }

            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Name = AuthHeaders.ApiKey,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "API Key for authentication."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
                    },
                    Array.Empty<string>()
                }
            });

            var jwtConfig = builder.Configuration
                .GetSection(JwtPublicConfiguration.SectionName)
                .Get<JwtPublicConfiguration>();

            if (!string.IsNullOrWhiteSpace(jwtConfig?.PublicKeyPath))
            {
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = AuthHeaders.Authorization,
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "JWT Bearer token."
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                        },
                        Array.Empty<string>()
                    }
                });
            }

            options.OperationFilter<AuthorizationOperationFilter>();
            options.OperationFilter<PermissionsOperationFilter>();
            options.SchemaFilter<SchemaValuesFromFilter>();
            options.DocumentFilter<VersionedDocumentFilter>();
            options.DocumentFilter<ScrubDescriptionsFilter>();
//#if (FeatureFlags)
            // An action behind a closed [FeatureGate] answers 404, so the document leaves it out too.
            options.OperationFilter<FeatureGateFilters>();
            options.DocumentFilter<FeatureGateFilters>();
//#endif

            var xmlFile = $"{serviceAssembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            // Read through a stream: given a path, the XML reader resolves it as a URI, and a Windows path
            // longer than 260 characters fails to parse as one.
            if (File.Exists(xmlPath))
                options.IncludeXmlComments(() =>
                {
                    using var stream = File.OpenRead(xmlPath);
                    return new XPathDocument(stream);
                }, true);

            options.DescribeAllParametersInCamelCase();
            options.SupportNonNullableReferenceTypes();

            configure?.Invoke(options);
        });

        return builder;
    }

    /// <summary>
    /// Adds Swagger UI middleware (disabled in Production).
    /// </summary>
    public static WebApplication UseSwaggerPage(this WebApplication app, Assembly serviceAssembly)
    {
        if (app.Environment.IsProduction()) return app;

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            var displayName = app.Configuration.GetValue<string>("Application:ShortName") ?? string.Empty;
            c.DocumentTitle = displayName;

            c.SwaggerEndpoint("/swagger/all/swagger.json", $"{displayName} - All");

            var versions = ApiVersionHelper.DiscoverAllVersions(serviceAssembly);
            foreach (var version in versions)
                c.SwaggerEndpoint($"/swagger/{version}/swagger.json", $"{displayName} - {version.ToUpper()}");

            // The gateway lists the documents of the services behind it too (Gateway/GatewaySwagger).
            foreach (var document in app.Services.GetServices<ServiceSwaggerDocument>())
                c.SwaggerEndpoint(document.Url, document.Name);

            c.ConfigObject.AdditionalItems["persistAuthorization"] = true;
            c.DisplayOperationId();
            c.EnableTryItOutByDefault();
        });

        return app;
    }
}
