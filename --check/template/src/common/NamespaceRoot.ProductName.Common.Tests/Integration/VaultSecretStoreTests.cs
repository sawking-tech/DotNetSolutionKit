using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// The Vault store against a real Vault, from <c>TEST_VAULT</c>: <c>Address=http://localhost:8200;Token=...</c>,
/// a token that may write to the <c>secret</c> KV version 2 mount (a dev server's root token). Skipped when
/// it is not set.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
internal class VaultSecretStoreTests
{
    private VaultOptions _options = null!;
    private string _prefix = null!;

    [SetUp]
    public void Connect()
    {
        var value = Environment.GetEnvironmentVariable("TEST_VAULT");
        if (string.IsNullOrWhiteSpace(value))
            Assert.Ignore("TEST_VAULT is not set: no Vault to run against.");

        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
        _options = new VaultOptions { Address = parts["Address"], Token = parts["Token"] };
        _prefix = $"tests-{Guid.NewGuid():N}";
    }

    private async Task Write(string path, Dictionary<string, object> values)
    {
        var client = new VaultClient(new VaultClientSettings(_options.Address, new TokenAuthMethodInfo(_options.Token)));
        await client.V1.Secrets.KeyValue.V2.WriteSecretAsync($"{_prefix}/{path}", values, mountPoint: _options.Mount);
    }

    [Test]
    public async Task A_secret_reads_as_its_keys()
    {
        await Write("auth", new() { ["ConnectionStrings__DefaultConnection"] = "Host=db", ["Port"] = 5432 });

        var secrets = await new VaultSecretStore(_options).ReadAsync($"{_prefix}/auth");

        secrets["ConnectionStrings__DefaultConnection"].ShouldBe("Host=db");
        secrets["Port"].ShouldBe("5432");
    }

    [Test]
    public async Task A_path_without_a_secret_reads_as_empty()
    {
        (await new VaultSecretStore(_options).ReadAsync($"{_prefix}/nothing-here")).ShouldBeEmpty();
    }

    [Test]
    public async Task A_wrong_token_is_a_configuration_failure()
    {
        var wrong = new VaultOptions { Address = _options.Address, Token = "test-do-not-use-wrong" };

        await Should.ThrowAsync<ConfigurationException>(() => new VaultSecretStore(wrong).ReadAsync($"{_prefix}/auth"));
    }
}
