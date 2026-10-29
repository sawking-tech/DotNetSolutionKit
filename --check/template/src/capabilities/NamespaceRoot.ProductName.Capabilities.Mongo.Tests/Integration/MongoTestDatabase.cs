using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace NamespaceRoot.ProductName.Capabilities.Mongo.Tests.Integration;

/// <summary>
/// A MongoDB database of the test's own, on the server named by <c>TEST_MONGO</c>, dropped when the test
/// is done. MongoDB creates a database on its first write, so there is nothing to set up.
/// </summary>
/// <remarks>
/// Service tests do not reach MongoDB at all: readers and writers sit behind the service's ports, and a
/// service test uses an in-memory double. What runs here is the driver against a real server.
/// </remarks>
public sealed class MongoTestDatabase : IAsyncDisposable
{
    public const string Variable = "TEST_MONGO";

    private readonly string _server;

    private MongoTestDatabase(string server, string name)
    {
        _server = server;
        Name = name;
    }

    public string Name { get; }

    /// <summary>Names the database; skips the test when <c>TEST_MONGO</c> is not set.</summary>
    public static MongoTestDatabase Create()
    {
        var server = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(server))
            Assert.Ignore($"{Variable} is not set: no MongoDB to run integration tests against.");

        return new MongoTestDatabase(server!, $"test_{Guid.NewGuid():N}");
    }

    /// <summary>The platform's MongoDB services, pointed at this database.</summary>
    public ServiceProvider Services(bool enabled = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDB:Enabled"] = enabled.ToString(),
            ["MongoDB:ConnectionString"] = _server,
            ["MongoDB:Database"] = Name,
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddMongoDB(configuration);
        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var client = new MongoClient(_server);
            await client.DropDatabaseAsync(Name);
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Could not drop test database {Name}: {ex.Message}");
        }
    }
}
