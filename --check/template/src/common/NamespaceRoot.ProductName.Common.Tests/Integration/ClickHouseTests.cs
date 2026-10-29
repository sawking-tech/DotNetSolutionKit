using ClickHouse.Client.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// The ClickHouse core against a real server: the schema check refuses a table the insert does not
/// fit, values come back through the shared coercion, readiness reports the server.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
internal class ClickHouseTests
{
    private static readonly string[] Columns = ["id", "amount", "at"];

    [Test(Description = "A table with every column the insert names passes")]
    public async Task Should_Pass_When_TheTableHasEveryColumn()
    {
        await using var db = await ClickHouseTestDatabase.CreateAsync();
        await db.ExecuteAsync("CREATE TABLE usage (id UInt64, amount Int64, at DateTime) ENGINE = MergeTree ORDER BY id");
        await using var services = db.Services();

        await services.GetRequiredService<ClickHouseSchemaGuard>()
            .EnsureColumnsAsync($"{db.Name}.usage", Columns, "Apply 001-usage.sql.");
    }

    [Test(Description = "A missing column stops the start, naming the column and the remedy")]
    public async Task Should_Refuse_When_AColumnIsMissing()
    {
        await using var db = await ClickHouseTestDatabase.CreateAsync();
        await db.ExecuteAsync("CREATE TABLE usage (id UInt64, at DateTime) ENGINE = MergeTree ORDER BY id");
        await using var services = db.Services();

        var error = await Should.ThrowAsync<InvalidOperationException>(() => services.GetRequiredService<ClickHouseSchemaGuard>()
            .EnsureColumnsAsync($"{db.Name}.usage", Columns, "Apply 002-amount.sql."));

        error.Message.ShouldContain("amount");
        error.Message.ShouldContain("002-amount.sql");
    }

    [Test(Description = "A missing table is told apart from a missing column")]
    public async Task Should_Refuse_When_TheTableIsMissing()
    {
        await using var db = await ClickHouseTestDatabase.CreateAsync();
        await using var services = db.Services();

        var error = await Should.ThrowAsync<InvalidOperationException>(() => services.GetRequiredService<ClickHouseSchemaGuard>()
            .EnsureColumnsAsync($"{db.Name}.usage", Columns, "Apply 001-usage.sql."));

        error.Message.ShouldContain("does not exist");
    }

    [Test(Description = "A paged read binds its parameters and the values come back typed")]
    public async Task Should_ReadAPageThroughTheSharedCoercion()
    {
        await using var db = await ClickHouseTestDatabase.CreateAsync();
        await db.ExecuteAsync("CREATE TABLE usage (id UInt64, amount Int64, active UInt8) ENGINE = MergeTree ORDER BY id");
        await db.ExecuteAsync("INSERT INTO usage VALUES (1, 10, 1), (2, 20, 0), (3, 30, 1)");
        await using var services = db.Services();

        await using var connection = services.GetRequiredService<IClickHouseConnections>().Create();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, amount, active FROM usage ORDER BY id LIMIT {p_page_size:UInt32} OFFSET {p_offset:UInt64}";
        ClickHouseValues.ApplyPagination(command, page: 2, pageSize: 2);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        ClickHouseValues.ToLong(reader.GetValue(0)).ShouldBe(3);
        ClickHouseValues.ToLong(reader.GetValue(1)).ShouldBe(30);
        ClickHouseValues.ToBool(reader.GetValue(2)).ShouldBeTrue();
        (await reader.ReadAsync()).ShouldBeFalse();
    }

    [Test(Description = "Readiness reports a reachable server")]
    public async Task Should_ReportReady()
    {
        await using var db = await ClickHouseTestDatabase.CreateAsync();
        await using var services = db.Services();

        var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries["clickhouse"].Status.ShouldBe(HealthStatus.Healthy);
    }

    [Test(Description = "Switched off, a connection answers 503 and the schema check is skipped")]
    public async Task Should_AnswerServiceUnavailable_When_SwitchedOff()
    {
        await using var db = await ClickHouseTestDatabase.CreateAsync();
        await using var services = db.Services(enabled: false);

        Should.Throw<ServiceUnavailableException>(() => services.GetRequiredService<IClickHouseConnections>().Create());
        await services.GetRequiredService<ClickHouseSchemaGuard>().EnsureColumnsAsync("missing.table", Columns, "");
    }
}
