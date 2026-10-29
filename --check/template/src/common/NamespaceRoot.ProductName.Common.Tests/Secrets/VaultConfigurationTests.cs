using Microsoft.Extensions.Configuration;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;
using NUnit.Framework;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Secrets;

/// <summary>
/// Vault reaches configuration by the same rules as any secret store: its own section, the shared path
/// read before the service's, and a refusal to start without it.
/// </summary>
[TestFixture]
public class VaultConfigurationTests
{
    private const string Service = "auth";

    private static Dictionary<string, string?> Configured() => new()
    {
        ["Vault:Address"] = "http://vault:8200",
        ["Vault:Token"] = "test-do-not-use",
    };

    [Test]
    public void The_shared_secret_is_read_first_and_the_service_secret_wins()
    {
        var store = new FakeStore(
            ("shared", new Dictionary<string, string> { ["Mail__Host"] = "shared-mail", ["Region"] = "eu" }),
            (Service, new Dictionary<string, string> { ["Mail__Host"] = "auth-mail" }));
        var builder = new ConfigurationBuilder().AddInMemoryCollection(Configured());

        var configuration = builder.AddPlatformVaultSecrets(Service, storeFactory: _ => store).Build();

        store.ReadPaths.ShouldBe(["shared", Service]);
        configuration["Mail:Host"].ShouldBe("auth-mail");
        configuration["Region"].ShouldBe("eu");
    }

    [Test]
    public void Without_an_address_and_a_sign_in_the_service_does_not_start()
    {
        var builder = new ConfigurationBuilder();

        var start = () => builder.AddPlatformVaultSecrets(Service, storeFactory: _ => new FakeStore()).Build();

        Should.Throw<ConfigurationException>(start);
    }

    [Test]
    public void An_unreachable_vault_stops_the_service()
    {
        var start = () => new ConfigurationBuilder().AddInMemoryCollection(Configured())
            .AddPlatformVaultSecrets(Service, storeFactory: _ => new UnreachableStore())
            .Build();

        Should.Throw<ConfigurationException>(start);
    }

    [Test]
    public void Switched_off_it_reads_nothing()
    {
        var store = new FakeStore();
        var settings = Configured();
        settings["Vault:Enabled"] = "false";

        new ConfigurationBuilder().AddInMemoryCollection(settings)
            .AddPlatformVaultSecrets(Service, storeFactory: _ => store)
            .Build();

        store.ReadPaths.ShouldBeEmpty();
    }

    private sealed class FakeStore(params (string Path, Dictionary<string, string> Secrets)[] secrets) : ISecretStore
    {
        public List<string> ReadPaths { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            ReadPaths.Add(path);
            var found = secrets.FirstOrDefault(s => s.Path == path).Secrets;
            return Task.FromResult<IReadOnlyDictionary<string, string>>(found ?? new Dictionary<string, string>());
        }
    }

    private sealed class UnreachableStore : ISecretStore
    {
        public Task<IReadOnlyDictionary<string, string>> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            throw new ConfigurationException("Vault is down");
    }
}
