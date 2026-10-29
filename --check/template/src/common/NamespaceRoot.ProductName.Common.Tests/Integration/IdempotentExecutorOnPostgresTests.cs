using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

[TestFixture]
[Category(TestCategories.Integration)]
internal sealed class IdempotentExecutorOnPostgresTests : IdempotentExecutorTests
{
    protected override async Task<string> CreateDatabaseAsync(string name)
    {
        var admin = Postgres.ConnectionString();
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection).ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(admin) { Database = name }.ToString();
    }

    protected override async Task DropDatabaseAsync(string name)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(Postgres.ConnectionString());
        await connection.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection).ExecuteNonQueryAsync();
    }

    protected override DbContextOptions<Db> Options(string connection) =>
        new DbContextOptionsBuilder<Db>().UseNpgsql(connection).Options;
}
