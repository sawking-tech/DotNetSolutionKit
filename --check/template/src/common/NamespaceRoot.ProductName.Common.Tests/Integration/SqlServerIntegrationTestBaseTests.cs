using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// The SQL Server test context: each test gets a database of its own, restored from one template per run,
/// and the database is gone after the test.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
internal class SqlServerIntegrationTestBaseTests
{
    private sealed class EmptyDb(DbContextOptions<EmptyDb> options) : DbContext(options);

    private sealed class Probe(EmptyDb db)
    {
        public Task<string> DatabaseAsync() =>
            db.Database.SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]").SingleAsync();
    }

    private sealed class Context : SqlServerIntegrationTestBase<Probe, EmptyDb>
    {
        private Context(string admin, string name, string connection, Action<IServiceCollection>? configure)
            : base(admin, name, connection, configure) { }

        public static Task<Context> CreateAsync() =>
            CreateCoreAsync<Context>(SqlServer.ConnectionString(), "common_test",
                (admin, name, connection, configure) => new Context(admin, name, connection, configure), null);
    }

    private static async Task<bool> ExistsAsync(string database)
    {
        await using var connection = new SqlConnection(SqlServer.ConnectionString());
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @name", connection);
        command.Parameters.AddWithValue("@name", database);
        return (int)(await command.ExecuteScalarAsync())! > 0;
    }

    [Test(Description = "Tests running together each get a database of their own")]
    public async Task Should_GiveEachTestItsOwnDatabase()
    {
        var databases = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var ctx = await Context.CreateAsync();
            return await ctx.ActAsync(probe => probe.DatabaseAsync());
        }));

        databases.Distinct().Count().ShouldBe(4);
    }

    [Test(Description = "A test's database is dropped when the test is done")]
    public async Task Should_DropTheDatabase_When_TheTestIsDone()
    {
        string database;
        await using (var ctx = await Context.CreateAsync())
        {
            database = await ctx.ActAsync(probe => probe.DatabaseAsync());
            (await ExistsAsync(database)).ShouldBeTrue();
        }

        (await ExistsAsync(database)).ShouldBeFalse();
    }
}
