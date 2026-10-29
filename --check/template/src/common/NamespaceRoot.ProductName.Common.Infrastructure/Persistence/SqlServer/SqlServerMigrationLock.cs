using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.SqlServer;

/// <summary>
/// The migration lock on SQL Server: an application lock owned by the session, released explicitly and by
/// the server when the session ends.
/// </summary>
/// <remarks>
/// The lock waits as long as the migrations of another replica take (<c>@LockTimeout = -1</c>); a result
/// below zero means it was not granted, which is a failure to report rather than a lock to assume.
/// </remarks>
public sealed class SqlServerMigrationLock : IMigrationLock
{
    public DbConnection Connect(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    public void Acquire(DbConnection connection, long key)
    {
        using var command = Procedure(connection, "sp_getapplock", key);
        command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
        command.Parameters.Add(new SqlParameter("@LockTimeout", -1));
        var result = command.Parameters.Add(new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue });
        command.ExecuteNonQuery();

        if ((int)result.Value < 0)
            throw new InvalidOperationException($"The migration lock {key} was not granted: sp_getapplock returned {result.Value}.");
    }

    public void Release(DbConnection connection, long key)
    {
        using var command = Procedure(connection, "sp_releaseapplock", key);
        command.ExecuteNonQuery();
    }

    private static SqlCommand Procedure(DbConnection connection, string name, long key)
    {
        var command = new SqlCommand(name, (SqlConnection)connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(new SqlParameter("@Resource", $"migrations:{key}"));
        command.Parameters.Add(new SqlParameter("@LockOwner", "Session"));
        return command;
    }
}
