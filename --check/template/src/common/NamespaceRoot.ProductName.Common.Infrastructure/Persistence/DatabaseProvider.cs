using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Persistence;
using NamespaceRoot.ProductName.Common.Domain.Specifications;
using NamespaceRoot.ProductName.Common.Infrastructure.Diagnostics;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Specifications;
//#if (Database != "mssql")
using Npgsql;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.Postgres;
//#endif
//#if (Database != "postgres")
using Microsoft.Data.SqlClient;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.SqlServer;
//#endif

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence;

/// <summary>
/// The database of the solution, PostgreSQL or SQL Server: the one place that knows which. A service and
/// <c>Common</c> ask it instead of naming a provider, so the choice made at generation, or a provider
/// added later, touches this file.
/// </summary>
/// <remarks>
/// A generated solution keeps one provider. The template's own sources keep both, PostgreSQL, the
/// default, first.
/// </remarks>
public static class DatabaseProvider
{
    /// <summary>The provider's name, as a health check and a log call it.</summary>
    public static string Name
    {
        get
        {
//#if (Database != "mssql")
            return "postgres";
//#endif
//#if (Database == "mssql")
            return "sqlserver";
//#endif
        }
    }

    /// <summary>True when the solution's database is SQL Server.</summary>
    public static bool IsSqlServer
    {
        get
        {
//#if (Database != "mssql")
            return false;
//#endif
//#if (Database == "mssql")
            return true;
//#endif
        }
    }

    /// <summary>
    /// A connection string that names no real server: enough for a design-time context, which adding a
    /// migration builds without connecting.
    /// </summary>
    public static string DesignTimeConnectionString
    {
        get
        {
//#if (Database != "mssql")
            return "Host=localhost;Database=design-time-placeholder";
//#endif
//#if (Database == "mssql")
            return "Server=localhost;Database=design-time-placeholder;TrustServerCertificate=true";
//#endif
        }
    }

    /// <summary>
    /// Points <paramref name="options"/> at the solution's database. With <paramref name="schema"/>, EF's
    /// migrations history lives in that schema, next to the tables it describes.
    /// </summary>
    public static DbContextOptionsBuilder UseDatabase(
        this DbContextOptionsBuilder options, string connectionString, string? schema = null)
    {
//#if (Database != "mssql")
        return options.UseNpgsql(connectionString, x =>
        {
            if (schema is not null)
                x.MigrationsHistoryTable("__EFMigrationsHistory", schema);
        });
//#endif
//#if (Database == "mssql")
        return options.UseSqlServer(connectionString, x =>
        {
            if (schema is not null)
                x.MigrationsHistoryTable("__EFMigrationsHistory", schema);
        });
//#endif
    }

    /// <summary>The server, database and user a connection string names, without its credentials.</summary>
    public static string DescribeTarget(string connectionString)
    {
//#if (Database != "mssql")
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{target.Host}:{target.Port}/{target.Database} as {target.Username ?? "(no user)"}";
//#endif
//#if (Database == "mssql")
        var target = new SqlConnectionStringBuilder(connectionString);
        return $"{target.DataSource}/{target.InitialCatalog} as {(string.IsNullOrEmpty(target.UserID) ? "(integrated)" : target.UserID)}";
//#endif
    }

    /// <summary>
    /// Ensures that <paramref name="schema"/> is either empty or owned by <paramref name="serviceName"/>,
    /// so two services never share one; throws otherwise.
    /// </summary>
    public static void EnsureExclusiveSchema(string connectionString, string schema, string serviceName)
    {
//#if (Database != "mssql")
        PostgresSchemaGuard.EnsureExclusiveSchema(connectionString, schema, serviceName);
//#endif
//#if (Database == "mssql")
        SqlServerSchemaGuard.EnsureExclusiveSchema(connectionString, schema, serviceName);
//#endif
    }

    /// <summary>
    /// Registers what a service's queries need from its database: case-insensitive search, and readable
    /// numbers from a sequence of <typeparamref name="TContext"/>.
    /// </summary>
    public static IServiceCollection AddDatabaseQueries<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
//#if (Database != "mssql")
        services.AddScoped<ICaseInsensitiveSearch, PostgresCaseInsensitiveSearch>();
        services.AddScoped<IShortIdGenerator, PostgresShortIdGenerator<TContext>>();
//#endif
//#if (Database == "mssql")
        services.AddScoped<ICaseInsensitiveSearch, SqlServerCaseInsensitiveSearch>();
        services.AddScoped<IShortIdGenerator, SqlServerShortIdGenerator<TContext>>();
//#endif
        return services;
    }

    /// <summary>True when a failed save broke a unique index.</summary>
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
//#if (Database != "mssql")
        return PostgresErrors.IsUniqueViolation(exception);
//#endif
//#if (Database == "mssql")
        return SqlServerErrors.IsUniqueViolation(exception);
//#endif
    }

    /// <summary>The lock statements the MassTransit outbox runs against the solution's database.</summary>
    public static IEntityFrameworkOutboxConfigurator UseDatabaseLocks(this IEntityFrameworkOutboxConfigurator outbox)
    {
//#if (Database != "mssql")
        outbox.UsePostgres();
//#endif
//#if (Database == "mssql")
        outbox.UseSqlServer();
//#endif
        return outbox;
    }

    /// <summary>How the outbox statistics are read from the solution's database.</summary>
    internal static IOutboxStatsDialect CreateOutboxStatsDialect()
    {
//#if (Database != "mssql")
        return new PostgresOutboxStatsDialect();
//#endif
//#if (Database == "mssql")
        return new SqlServerOutboxStatsDialect();
//#endif
    }

    /// <summary>The lock migrations take turns under, one at a time across every instance.</summary>
    public static IMigrationLock CreateMigrationLock()
    {
//#if (Database != "mssql")
        return new PostgresMigrationLock();
//#endif
//#if (Database == "mssql")
        return new SqlServerMigrationLock();
//#endif
    }
}
