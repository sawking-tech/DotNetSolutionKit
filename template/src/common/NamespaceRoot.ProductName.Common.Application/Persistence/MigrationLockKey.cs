using System.Security.Cryptography;
using System.Text;

namespace NamespaceRoot.ProductName.Common.Application.Persistence;

/// <summary>
/// The advisory-lock key of a migration scope: a stable 64-bit number hashed from the scope's name, so
/// every replica of every service computes the same key from the same name and the database hands the
/// lock to one of them.
/// </summary>
public static class MigrationLockKey
{
    /// <summary>Computes the lock key of <paramref name="scope"/>.</summary>
    public static long Compute(string scope)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope));
        return BitConverter.ToInt64(hash, 0);
    }
}
