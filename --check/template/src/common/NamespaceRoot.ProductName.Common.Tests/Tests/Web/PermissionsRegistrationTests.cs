using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Application.Authorization;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Tests.Stubs;
using NamespaceRoot.ProductName.Common.Web.Authorization;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// Permissions:Source picks the implementation, and a remote source without an address does not start.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class PermissionsRegistrationTests
{
    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext>(_ => new UserContextMock());
        services.AddPlatformPermissions(configuration);
        return services.BuildServiceProvider();
    }

    [Test]
    public void Without_a_source_the_permissions_come_from_the_token()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPermissionService>().ShouldBeOfType<ClaimsPermissionService>();
    }

    [Test]
    public void A_remote_source_asks_the_permission_service()
    {
        using var provider = Build(("Permissions:Source", "remote"), ("Permissions:Remote:BaseAddress", "http://permissions:8080/"));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPermissionService>().ShouldBeOfType<RemotePermissionService>();
    }

    [Test]
    public void A_remote_source_without_an_address_is_refused()
    {
        using var provider = Build(("Permissions:Source", "remote"));

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PermissionsOptions>>().Value);
        failure.Message.ShouldContain("Permissions:Remote:BaseAddress");
    }

    [Test]
    public void An_unknown_source_is_refused()
    {
        using var provider = Build(("Permissions:Source", "ldap"));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PermissionsOptions>>().Value);
    }
}
