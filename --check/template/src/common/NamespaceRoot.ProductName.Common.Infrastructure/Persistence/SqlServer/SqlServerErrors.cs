using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.SqlServer;

/// <summary>What a SQL Server error means to the platform.</summary>
public static class SqlServerErrors
{
    // 2627: a PRIMARY KEY or UNIQUE constraint; 2601: a unique index.
    private static readonly int[] UniqueViolationNumbers = [2627, 2601];

    /// <summary>An insert or update refused by a unique index or constraint.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && UniqueViolationNumbers.Contains(sql.Number);
}
