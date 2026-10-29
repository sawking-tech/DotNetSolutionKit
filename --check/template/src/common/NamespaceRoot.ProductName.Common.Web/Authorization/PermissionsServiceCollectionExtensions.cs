using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Application.Authorization;
using NamespaceRoot.ProductName.Common.Infrastructure.Http.Internal;

namespace NamespaceRoot.ProductName.Common.Web.Authorization;

public static class PermissionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers where the permissions come from, by <c>Permissions:Source</c>: the token's claims, or a
    /// separate service. A service that registers its own <see cref="IPermissionService"/> first keeps it.
    /// </summary>
    public static IServiceCollection AddPlatformPermissions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PermissionsOptions>()
            .BindConfiguration(PermissionsOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => !o.IsRemote || Uri.TryCreate(o.Remote.BaseAddress, UriKind.Absolute, out _),
                "Permissions:Remote:BaseAddress must be an absolute address when Permissions:Source is remote")
            .ValidateOnStart();

        var source = configuration[$"{PermissionsOptions.SectionName}:{nameof(PermissionsOptions.Source)}"];
        if (source != "remote")
        {
            services.TryAddScoped<IPermissionService, ClaimsPermissionService>();
            return services;
        }

        services.AddMemoryCache();
        services.AddHttpClient(RemotePermissionService.HttpClientName, (sp, client) =>
            {
                var remote = sp.GetRequiredService<IOptions<PermissionsOptions>>().Value.Remote;
                client.BaseAddress = new Uri(remote.BaseAddress);
            })
            .AddInternalServiceHandlers();
        services.TryAddScoped<IPermissionService, RemotePermissionService>();
        return services;
    }
}
