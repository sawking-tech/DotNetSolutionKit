using ClickHouse.Client.ADO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// A ClickHouse database of the test's own, on the server named by <c>TEST_CLICKHOUSE</c>, dropped when
/// the test is done. Creating one is cheap in ClickHouse, so there is no template: the test creates the
/// tables it needs with <see cref="ExecuteAsync"/>.
/// </summary>
/// <remarks>
/// Service tests do not reach ClickHouse at all: readers and writers sit behind the service's ports, and
/// a service test uses an in-memory double. What runs here is the SQL itself.
/// </remarks>
public sealed class ClickHouseTestDatabase : IAsyncDisposable
{
    public const string Variable = "TEST_CLICKHOUSE";

    private readonly string _server;

    private ClickHouseTestDatabase(string server, string name)
    {
        _server = server;
        Name = name;
        ConnectionString = $"{server};Database={name}";
    }

    public string Name { get; }

    public string ConnectionString { get; }

    /// <summary>Creates the database; skips the test when <c>TEST_CLICKHOUSE</c> is not set.</summary>
    public static async Task<ClickHouseTestDatabase> CreateAsync()
    {
        var server = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(server))
            Assert.Ignore($"{Variable} is not set: no ClickHouse to run integration tests against.");

        var database = new ClickHouseTestDatabase(server!, $"test_{Guid.NewGuid():N}");
        await database.OnServerAsync($"CREATE DATABASE {database.Name}");
        return database;
    }

    /// <summary>Runs a statement in this database: the DDL of the tables a test needs.</summary>
    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new ClickHouseConnection(ConnectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The platform's ClickHouse services, pointed at this database.</summary>
    public ServiceProvider Services(bool enabled = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClickHouse:Enabled"] = enabled.ToString(),
            ["ClickHouse:ConnectionString"] = ConnectionString,
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddClickHouse(configuration);
        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await OnServerAsync($"DROP DATABASE IF EXISTS {Name}");
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Could not drop test database {Name}: {ex.Message}");
        }
    }

    private async Task OnServerAsync(string sql)
    {
        await using var connection = new ClickHouseConnection(_server);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
