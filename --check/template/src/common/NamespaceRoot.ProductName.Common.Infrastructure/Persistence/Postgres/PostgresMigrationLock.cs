using System.Data.Common;
using Npgsql;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.Postgres;

/// <summary>
/// The migration lock on PostgreSQL: a session advisory lock, released explicitly and by the server when
/// the session ends.
/// </summary>
public sealed class PostgresMigrationLock : IMigrationLock
{
    public DbConnection Connect(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    public void Acquire(DbConnection connection, long key) => Run(connection, "SELECT pg_advisory_lock(@key)", key);

    public void Release(DbConnection connection, long key) => Run(connection, "SELECT pg_advisory_unlock(@key)", key);

    private static void Run(DbConnection connection, string sql, long key)
    {
        using var command = new NpgsqlCommand(sql, (NpgsqlConnection)connection);
        command.Parameters.AddWithValue("key", key);
        command.ExecuteNonQuery();
    }
}
