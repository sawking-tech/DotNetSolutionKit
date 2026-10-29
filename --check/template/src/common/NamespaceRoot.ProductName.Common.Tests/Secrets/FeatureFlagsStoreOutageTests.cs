using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using NamespaceRoot.ProductName.Common.Application.FeatureManagement;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.FeatureManagement;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;

namespace NamespaceRoot.ProductName.Common.Tests.Secrets;

/// <summary>
/// The feature flags of a service whose secret store answers, changes and goes down, end to end: the
/// shared file, the store's real configuration provider, and the catalogue that answers
/// <c>GET /api/v1/features</c> and <c>IFeatureManager</c>. Only the store itself is a fake.
/// </summary>
/// <remarks>
/// Each step on its own has tests of its own - the reload, the pin. These check that the steps
/// meet: that a flag set in the store reaches the catalogue, and that an outage of the store leaves the
/// flags as the feature flags guide says.
/// </remarks>
[TestFixture]
public class FeatureFlagsStoreOutageTests
{
    private const string Key = "orders.v2";
    private const string Secret = $"Features__{Key}__enabled";
    private string _folder = null!;

    private sealed class Store : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = new() { [Secret] = "true" };
        public bool Down { get; set; }

        public Task<IReadOnlyDictionary<string, string>> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Down
                ? throw new ConfigurationException("the store is down")
                : Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Values));
    }

    private sealed class Production : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed record Service(ConfigurationFeatureCatalog Catalog, SecretsConfigurationProvider Provider);

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("outage-").FullName;

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    /// <summary>A service starting: the shared file, the store above it; ReloadSeconds 0, the test reloads.</summary>
    private Service Start(Store store, bool pinned = false)
    {
        File.WriteAllText(Path.Combine(_folder, FeatureConfigurationExtensions.FileName), $$"""
            { "Features": { "{{Key}}": { "enabled": false, "pinned": {{pinned.ToString().ToLowerInvariant()}} } } }
            """);

        var builder = new ConfigurationBuilder()
            .AddJsonFile(new PhysicalFileProvider(_folder), FeatureConfigurationExtensions.FileName, optional: false, reloadOnChange: false);
//#if (Infisical)
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Infisical:ProjectId"] = "project", ["Infisical:EnvironmentSlug"] = "prod",
            ["Infisical:ClientId"] = "id", ["Infisical:ClientSecret"] = "secret",
            ["Infisical:ReloadSeconds"] = "0",
        });
        builder.AddPlatformSecrets("/orders", storeFactory: _ => store);
//#else
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Vault:Address"] = "http://vault:8200", ["Vault:Token"] = "test-do-not-use",
            ["Vault:ReloadSeconds"] = "0",
        });
        builder.AddPlatformVaultSecrets("orders", storeFactory: _ => store);
//#endif
        var configuration = builder.Build();

        return new Service(
            new ConfigurationFeatureCatalog(configuration, new Production(), TimeProvider.System),
            configuration.Providers.OfType<SecretsConfigurationProvider>().Last());
    }

    [Test(Description = "A flag set in the store decides over the shared file, and is reported as the store's")]
    public void The_store_decides_a_flag_the_file_also_declares()
    {
        var service = Start(new Store());

        var state = service.Catalog.Find(Key)!;
        state.Enabled.ShouldBeTrue("the file says off, the store says on");
        state.Source.ShouldBe(FeatureValueSource.Store);
    }

    [Test(Description = "A flag changed in the store applies on the next read, without a restart")]
    public async Task A_flag_changed_in_the_store_applies_without_a_restart()
    {
        var store = new Store();
        var service = Start(store);

        store.Values[Secret] = "false";
        await service.Provider.ReloadAsync();

        service.Catalog.Find(Key)!.Enabled.ShouldBeFalse();
    }

    [Test(Description = "A store that goes down while the service runs leaves the flags as they were")]
    public async Task A_store_that_goes_down_leaves_the_flags_as_they_were()
    {
        var store = new Store();
        var service = Start(store);

        store.Down = true;
        await service.Provider.ReloadAsync();

        service.Catalog.Find(Key)!.Enabled.ShouldBeTrue("the last values stay, not the file's");
    }

    [Test(Description = "A pinned flag takes the file's value over the store")]
    public void A_pinned_flag_takes_the_file_value_over_the_store()
    {
        var service = Start(new Store(), pinned: true);

        service.Catalog.Find(Key)!.Enabled.ShouldBeFalse();
        service.Catalog.Find(Key)!.Source.ShouldBe(FeatureValueSource.Pinned);
    }

    [Test(Description = "A service that starts while the store is down does not start: it has no secrets")]
    public void A_start_with_the_store_down_stops()
    {
        Should.Throw<ConfigurationException>(() => Start(new Store { Down = true }));
    }
}
