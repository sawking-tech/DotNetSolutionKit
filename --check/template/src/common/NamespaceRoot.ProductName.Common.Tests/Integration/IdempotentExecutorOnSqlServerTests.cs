using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

[TestFixture]
[Category(TestCategories.Integration)]
internal sealed class IdempotentExecutorOnSqlServerTests : IdempotentExecutorTests
{
    protected override async Task<string> CreateDatabaseAsync(string name)
    {
        var admin = SqlServer.ConnectionString();
        await using (var connection = new SqlConnection(admin))
        {
            await connection.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{name}]", connection).ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(admin) { InitialCatalog = name }.ConnectionString;
    }

    protected override async Task DropDatabaseAsync(string name)
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(SqlServer.ConnectionString());
        await connection.OpenAsync();
        await new SqlCommand(
            $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END",
            connection).ExecuteNonQueryAsync();
    }

    protected override DbContextOptions<Db> Options(string connection) =>
        new DbContextOptionsBuilder<Db>().UseSqlServer(connection).Options;
}
