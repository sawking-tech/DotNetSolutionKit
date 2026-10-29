using Microsoft.Extensions.Configuration;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;
using NUnit.Framework;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Secrets;

/// <summary>
/// A running service reads the store again: a changed value reaches configuration and raises its change
/// token, and a store that stops answering leaves the values as they were.
/// </summary>
[TestFixture]
public class SecretsReloadTests
{
    private sealed class ChangingStore : ISecretStore
    {
        public Dictionary<string, string> Values { get; set; } = new() { ["Limits__PageSize"] = "50" };
        public bool Down { get; set; }

        public Task<IReadOnlyDictionary<string, string>> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Down
                ? throw new ConfigurationException("the store is down")
                : Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Values));
    }

    // ReloadSeconds is 0, so no timer runs and the test calls the reload itself.
    private static (IConfigurationRoot Configuration, SecretsConfigurationProvider Provider) Build(ChangingStore store)
    {
        var builder = new ConfigurationBuilder();
//#if (Infisical)
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Infisical:ProjectId"] = "project", ["Infisical:EnvironmentSlug"] = "prod",
            ["Infisical:ClientId"] = "id", ["Infisical:ClientSecret"] = "secret", ["Infisical:ReloadSeconds"] = "0",
        });
        builder.AddPlatformSecrets("/orders", storeFactory: _ => store);
//#else
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Vault:Address"] = "http://vault:8200", ["Vault:Token"] = "test-do-not-use", ["Vault:ReloadSeconds"] = "0",
        });
        builder.AddPlatformVaultSecrets("orders", storeFactory: _ => store);
//#endif
        var configuration = builder.Build();
        return (configuration, configuration.Providers.OfType<SecretsConfigurationProvider>().Last());
    }

    [Test]
    public async Task A_value_changed_in_the_store_reaches_configuration_and_raises_the_token()
    {
        var store = new ChangingStore();
        var (configuration, provider) = Build(store);
        var raised = false;
        configuration.GetReloadToken().RegisterChangeCallback(_ => raised = true, null);

        store.Values["Limits__PageSize"] = "100";
        await provider.ReloadAsync();

        configuration["Limits:PageSize"].ShouldBe("100");
        raised.ShouldBeTrue();
    }

    [Test]
    public async Task The_same_values_raise_nothing()
    {
        var store = new ChangingStore();
        var (configuration, provider) = Build(store);
        var raised = false;
        configuration.GetReloadToken().RegisterChangeCallback(_ => raised = true, null);

        await provider.ReloadAsync();

        raised.ShouldBeFalse();
    }

    [Test]
    public async Task A_store_that_stops_answering_leaves_the_values_and_says_so()
    {
        var store = new ChangingStore();
        var (configuration, provider) = Build(store);

        store.Down = true;
        await provider.ReloadAsync();

        configuration["Limits:PageSize"].ShouldBe("50");
        configuration[SecretsConfigurationProvider.ReloadErrorKey].ShouldNotBeNull().ShouldContain("the store is down");
    }
}
