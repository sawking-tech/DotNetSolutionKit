using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// The PostgreSQL test context: each test gets a database of its own, cloned from one template per run,
/// and the database is gone after the test.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
internal class PostgresIntegrationTestBaseTests
{
    private sealed class EmptyDb(DbContextOptions<EmptyDb> options) : DbContext(options);

    private sealed class Probe(EmptyDb db)
    {
        public Task<string> DatabaseAsync() =>
            db.Database.SqlQueryRaw<string>("SELECT current_database() AS \"Value\"").SingleAsync();
    }

    private sealed class Context : PostgresIntegrationTestBase<Probe, EmptyDb>
    {
        private Context(string admin, string name, string connection, Action<IServiceCollection>? configure)
            : base(admin, name, connection, configure) { }

        public static Task<Context> CreateAsync() =>
            CreateCoreAsync<Context>(Postgres.ConnectionString(), "common_test",
                (admin, name, connection, configure) => new Context(admin, name, connection, configure), null);
    }

    private static async Task<bool> ExistsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(Postgres.ConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", database);
        return (long)(await command.ExecuteScalarAsync())! > 0;
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
