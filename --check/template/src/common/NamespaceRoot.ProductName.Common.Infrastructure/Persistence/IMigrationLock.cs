using System.Data.Common;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence;

/// <summary>
/// A lock that lets one replica of a service migrate its schema while the others wait: taken on its own
/// connection before the migrations, released after them, and gone with the connection if the process dies.
/// </summary>
public interface IMigrationLock
{
    /// <summary>Opens the lock's connection to the database of <paramref name="connectionString"/>.</summary>
    DbConnection Connect(string connectionString);

    /// <summary>Waits until the lock named by <paramref name="key"/> is this session's.</summary>
    void Acquire(DbConnection connection, long key);

    /// <summary>Gives the lock back before the connection closes.</summary>
    void Release(DbConnection connection, long key);
}
