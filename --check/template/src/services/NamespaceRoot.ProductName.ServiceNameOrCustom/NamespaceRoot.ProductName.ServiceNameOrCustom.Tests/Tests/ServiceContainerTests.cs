using NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Tests.Tests;

/// <summary>
/// The service's container is complete: every registered service can be built with what is registered.
/// </summary>
/// <remarks>
/// A missing registration otherwise shows up on the first request or job that needs it, in whatever
/// environment that happens. Building the application here runs the same validation the service runs at
/// startup, without a database, a broker or a secret store: they are switched off in configuration, which
/// keeps their code registered and leaves out only what would connect.
///
/// The switches are environment variables because the service reads them after its files; they are set
/// for the duration of the test, so the fixture does not run in parallel with others.
/// </remarks>
[RunsAlone]
public sealed class ServiceContainerTests : IDisposable
{
    private static readonly Dictionary<string, string> SwitchedOff = new()
    {
        ["Database__Enabled"] = "false",
        ["RabbitMq__Enabled"] = "false",
        ["Infisical__Enabled"] = "false",
        ["Vault__Enabled"] = "false",
        ["S3__Enabled"] = "false",
        ["ClickHouse__Enabled"] = "false",
        ["MongoDB__Enabled"] = "false",
    };

    private readonly Dictionary<string, string?> _previous = new();

    public ServiceContainerTests()
    {
        foreach (var (name, value) in SwitchedOff)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _previous)
            Environment.SetEnvironmentVariable(name, value);
    }

    [Test(Description = "Every registered service resolves, as the service checks at startup")]
    public async Task Should_BuildTheContainer()
    {
        await using var app = SchemaHost.Build([], AppContext.BaseDirectory);

        app.Services.ShouldNotBeNull();
    }
}
