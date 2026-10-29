using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;

namespace NamespaceRoot.ProductName.Capabilities.Mongo.Tests.Integration;

/// <summary>
/// The MongoDB core against a real server: a document goes in and comes back, a Guid keeps its value,
/// readiness reports the server.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
internal class MongoTests
{
    private sealed record Order(Guid Id, string Number, decimal Total, DateTime PlacedAt);

    [Test(Description = "A document written to a collection reads back as it was, its Guid included")]
    public async Task Should_ReadBackWhatWasWritten()
    {
        await using var db = MongoTestDatabase.Create();
        await using var services = db.Services();
        var orders = services.GetRequiredService<IMongoStore>().Collection<Order>("orders");
        var order = new Order(Guid.NewGuid(), "A-1", 12.50m, new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc));

        await orders.InsertOneAsync(order);
        var read = await orders.Find(o => o.Id == order.Id).SingleAsync();

        read.ShouldBe(order);
    }

    [Test(Description = "Readiness is healthy while the server answers")]
    public async Task Should_ReportReady_When_TheServerAnswers()
    {
        await using var db = MongoTestDatabase.Create();
        await using var services = db.Services();

        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries["mongo"].Status.ShouldBe(HealthStatus.Healthy);
    }
}
