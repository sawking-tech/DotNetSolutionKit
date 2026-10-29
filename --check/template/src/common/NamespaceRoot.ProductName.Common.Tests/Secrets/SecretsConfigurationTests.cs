using Microsoft.Extensions.Configuration;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;
using NUnit.Framework;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Secrets;

/// <summary>
/// How secrets become configuration: which folder wins, how a secret name becomes a configuration path,
/// and what a service does when the store cannot be read. The network call itself belongs to the vendor's
/// client and is not what breaks; these rules are.
/// </summary>
[TestFixture]
public class SecretsConfigurationTests
{
    private const string Shared = "/";
    private const string Service = "/auth";

    [Test]
    public void A_double_underscore_becomes_a_configuration_path()
    {
        var configuration = Build(new FakeStore(
            (Shared, new Dictionary<string, string> { ["ConnectionStrings__DefaultConnection"] = "Host=db" })));

        configuration["ConnectionStrings:DefaultConnection"].ShouldBe("Host=db",
            "a colon cannot appear in a secret name, so the same spelling as environment variables is used");
    }

    [Test]
    public void A_name_without_separators_is_read_as_is()
    {
        var configuration = Build(new FakeStore(
            (Shared, new Dictionary<string, string> { ["ApiKey"] = "abc" })));

        configuration["ApiKey"].ShouldBe("abc");
    }

    [Test]
    public void The_service_folder_overrides_the_shared_one()
    {
        var configuration = Build(new FakeStore(
            (Shared, new Dictionary<string, string> { ["Jwt__Issuer"] = "shared-issuer" }),
            (Service, new Dictionary<string, string> { ["Jwt__Issuer"] = "auth-issuer" })));

        configuration["Jwt:Issuer"].ShouldBe("auth-issuer",
            "a service overrides a shared value without the shared folder knowing which services exist");
    }

    [Test]
    public void Shared_values_the_service_does_not_override_are_kept()
    {
        var configuration = Build(new FakeStore(
            (Shared, new Dictionary<string, string> { ["Jwt__Issuer"] = "shared", ["Otlp__Endpoint"] = "http://otel" }),
            (Service, new Dictionary<string, string> { ["Jwt__Issuer"] = "auth" })));

        configuration["Otlp:Endpoint"].ShouldBe("http://otel");
    }

    [Test]
    public void Secrets_win_over_values_shipped_in_the_image()
    {
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Infisical:ProjectId"] = "project",
            ["Infisical:EnvironmentSlug"] = "prod",
            ["Infisical:ClientId"] = "id",
            ["Infisical:ClientSecret"] = "secret",
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost",
        });

        var store = new FakeStore(
            (Shared, new Dictionary<string, string> { ["ConnectionStrings__DefaultConnection"] = "Host=prod-db" }));
        builder.AddPlatformSecrets(Service, optional: false, storeFactory: _ => store);

        builder.Build()["ConnectionStrings:DefaultConnection"].ShouldBe("Host=prod-db",
            "a file baked into the image must not shadow what the environment was given");
    }

    [Test]
    public void A_service_refuses_to_start_when_the_store_is_not_configured()
    {
        var start = () => Build(new FakeStore(), configured: false);

        Should.Throw<ConfigurationException>(start, "a service without its secrets misbehaves rather than fails");
    }

    [Test]
    public void A_developer_may_run_without_the_store_when_it_is_explicitly_optional()
    {
        var configuration = Build(new FakeStore(), configured: false, optional: true);

        configuration["anything"].ShouldBeNull();
    }

    [Test]
    public void An_unreachable_store_stops_a_service_that_needs_it()
    {
        var start = () => Build(new UnreachableStore());

        Should.Throw<ConfigurationException>(start);
    }

    [Test]
    public void An_unreachable_store_is_survivable_only_when_declared_optional()
    {
        var configuration = Build(new UnreachableStore(), optional: true);

        configuration["anything"].ShouldBeNull();
    }

    [Test]
    public void Configuration_can_force_the_store_to_be_required()
    {
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Infisical:Optional"] = "false",
        });

        var start = () => builder.AddPlatformSecrets(Service, optional: true, storeFactory: _ => new FakeStore()).Build();

        Should.Throw<ConfigurationException>(start,
            "k8s sets Infisical__Optional=false so a Development host still refuses to start without the store");
    }

    [Test]
    public void The_shared_folder_is_not_read_twice_when_a_service_points_at_it()
    {
        var store = new FakeStore((Shared, new Dictionary<string, string> { ["ApiKey"] = "abc" }));

        Build(store, servicePath: Shared);

        store.ReadPaths.ShouldHaveSingleItem().ShouldBe(Shared);
    }

    private static FakeStore Store(string connection) =>
        new((Shared, new Dictionary<string, string> { ["ConnectionStrings__DefaultConnection"] = connection }));

    private static IConfiguration Build(
        ISecretStore store,
        bool configured = true,
        bool optional = false,
        string servicePath = Service)
    {
        var builder = new ConfigurationBuilder();

        if (configured)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Infisical:ProjectId"] = "project",
                ["Infisical:EnvironmentSlug"] = "prod",
                ["Infisical:ClientId"] = "id",
                ["Infisical:ClientSecret"] = "secret",
            });
        }

        builder.AddPlatformSecrets(servicePath, optional, _ => store);

        return builder.Build();
    }

    private sealed class FakeStore : ISecretStore
    {
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _folders;

        public FakeStore(params (string Path, Dictionary<string, string> Secrets)[] folders) =>
            _folders = folders.ToDictionary(
                folder => folder.Path,
                folder => (IReadOnlyDictionary<string, string>)folder.Secrets);

        public List<string> ReadPaths { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> ReadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            ReadPaths.Add(path);

            return Task.FromResult(_folders.TryGetValue(path, out var secrets)
                ? secrets
                : new Dictionary<string, string>());
        }
    }

    private sealed class UnreachableStore : ISecretStore
    {
        public Task<IReadOnlyDictionary<string, string>> ReadAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            throw new ConfigurationException($"Failed to read secrets from '{path}'.");
    }
}
