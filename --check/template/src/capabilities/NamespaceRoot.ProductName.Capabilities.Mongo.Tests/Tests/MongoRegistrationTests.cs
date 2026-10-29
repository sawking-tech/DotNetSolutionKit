using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Capabilities.Mongo.Tests;

/// <summary>
/// What the MongoDB registration requires and what it does switched off, without a server.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class MongoRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        // The host registers health checks for every service; the check MongoDB adds joins them.
        services.AddHealthChecks();
        services.AddMongoDB(configuration);
        return services.BuildServiceProvider();
    }

    [Test(Description = "Switched on, the settings name a connection string and a database")]
    public void Should_RequireTheConnectionAndTheDatabase_When_Enabled()
    {
        using var provider = Build(new() { ["MongoDB:Enabled"] = "true" });

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MongoOptions>>().Value);

        failure.Message.ShouldContain("MongoDB:ConnectionString");
        failure.Message.ShouldContain("MongoDB:Database");
    }

    [Test(Description = "Switched off, nothing is required and the database answers 503")]
    public void Should_AnswerUnavailable_When_SwitchedOff()
    {
        using var provider = Build(new() { ["MongoDB:Enabled"] = "false" });

        var store = provider.GetRequiredService<IMongoStore>();

        Should.Throw<ServiceUnavailableException>(() => store.Database);
        provider.GetRequiredService<IOptions<MongoOptions>>().Value.Enabled.ShouldBeFalse();
    }

    [Test(Description = "A connection string that is not one stops the start")]
    public void Should_RefuseAMalformedConnectionString()
    {
        using var provider = Build(new()
        {
            ["MongoDB:ConnectionString"] = "localhost:27017",
            ["MongoDB:Database"] = "orders",
        });

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MongoOptions>>().Value);

        failure.Message.ShouldContain("not a MongoDB connection string");
    }

    [Test(Description = "Switched off, readiness does not ask for MongoDB")]
    public async Task Should_LeaveReadinessAlone_When_SwitchedOff()
    {
        await using var provider = Build(new() { ["MongoDB:Enabled"] = "false" });

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries.ContainsKey("mongo").ShouldBeFalse();
    }

    [Test(Description = "Switched on, the database is the one the settings name")]
    public void Should_UseTheDatabaseOfTheSettings()
    {
        using var provider = Build(new()
        {
            ["MongoDB:ConnectionString"] = "mongodb://localhost:27017",
            ["MongoDB:Database"] = "orders",
        });

        provider.GetRequiredService<IMongoStore>().Database.DatabaseNamespace.DatabaseName.ShouldBe("orders");
    }
}
