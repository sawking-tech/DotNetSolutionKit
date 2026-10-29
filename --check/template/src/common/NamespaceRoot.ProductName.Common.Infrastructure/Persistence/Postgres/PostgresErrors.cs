using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.Postgres;

/// <summary>What a PostgreSQL error means to the platform.</summary>
public static class PostgresErrors
{
    /// <summary>An insert or update refused by a unique index or constraint (SQLSTATE 23505).</summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
