// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

namespace NamespaceRoot.ProductName.Common.Tests;

/// <summary>
/// SQL Server admin operations used by the template database and the per-test databases.
/// </summary>
internal static class SqlServerAdmin
{
    // A database name goes into the text of DDL, so it is quoted the way SQL Server quotes an identifier.
    public static string Quote(string name) => $"[{name.Replace("]", "]]")}]";

    public static async Task ExecuteAsync(SqlConnection connection, string sql, int timeoutSeconds = 120)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<SqlConnection> OpenAsync(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public static Task DropDatabaseAsync(SqlConnection connection, string name) =>
        ExecuteAsync(connection,
            $"IF DB_ID(N'{name}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE {Quote(name)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Quote(name)}; END",
            timeoutSeconds: 30);

    /// <summary>A connection string to <paramref name="database"/> on the server of <paramref name="adminConnStr"/>.</summary>
    public static string BuildDbConnStr(string adminConnStr, string database, int maxPoolSize) =>
        new SqlConnectionStringBuilder(adminConnStr)
        {
            InitialCatalog = database,
            MaxPoolSize = maxPoolSize,
            ConnectTimeout = 60,
        }.ConnectionString;
}

/// <summary>
/// One migrated template database per process for each (server, prefix) pair, and its backup.
/// </summary>
/// <remarks>
/// SQL Server has no <c>CREATE DATABASE ... TEMPLATE</c>; restoring a backup of the migrated database under
/// another name, with its files moved, gives each test the same schema without running a migration again.
/// The backup lives in the server's own default backup folder, so the test process needs no access to the
/// server's file system.
/// </remarks>
public static class SqlServerTemplateManager
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<SqlServerTemplate>>> Templates = new();

    public static async Task<SqlServerTemplate> GetOrCreateAsync(string adminConnStr, string dbPrefix, Func<string, Task> migrate)
    {
        var key = $"{adminConnStr}||{dbPrefix}";
        var lazy = Templates.GetOrAdd(key, _ => new Lazy<Task<SqlServerTemplate>>(
            () => CreateTemplateAsync(adminConnStr, dbPrefix, migrate), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value;
        }
        catch
        {
            // Don't cache failures: let the next test retry the template.
            Templates.TryRemove(key, out _);
            throw;
        }
    }

    private static async Task<SqlServerTemplate> CreateTemplateAsync(string adminConnStr, string dbPrefix, Func<string, Task> migrate)
    {
        var name = $"{dbPrefix}_tmpl_{Environment.ProcessId}";
        await using var connection = await SqlServerAdmin.OpenAsync(adminConnStr);

        await SqlServerAdmin.DropDatabaseAsync(connection, name);
        await SqlServerAdmin.ExecuteAsync(connection, $"CREATE DATABASE {SqlServerAdmin.Quote(name)}");
        await migrate(SqlServerAdmin.BuildDbConnStr(adminConnStr, name, maxPoolSize: 10));
        SqlConnection.ClearAllPools();

        var backupFolder = await ScalarAsync(connection, "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))");
        var dataFolder = await ScalarAsync(connection, "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))");
        var separator = backupFolder.Contains('\\') ? "\\" : "/";
        var backup = $"{backupFolder.TrimEnd('/', '\\')}{separator}{name}.bak";
        await SqlServerAdmin.ExecuteAsync(connection,
            $"BACKUP DATABASE {SqlServerAdmin.Quote(name)} TO DISK = N'{backup}' WITH INIT, COPY_ONLY");

        var files = new List<(string Logical, bool IsLog)>();
        await using (var command = new SqlCommand(
                         "SELECT name, type FROM sys.master_files WHERE database_id = DB_ID(@name)", connection))
        {
            command.Parameters.AddWithValue("@name", name);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                files.Add((reader.GetString(0), reader.GetByte(1) == 1));
        }

        return new SqlServerTemplate(name, backup, dataFolder.TrimEnd('/', '\\') + separator, files);
    }

    private static async Task<string> ScalarAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}

/// <summary>A migrated template database, its backup and the files a restore of it moves.</summary>
public sealed record SqlServerTemplate(string Name, string Backup, string DataFolder, IReadOnlyList<(string Logical, bool IsLog)> Files)
{
    /// <summary>The statement that restores the template as <paramref name="database"/>, with files of its own.</summary>
    public string RestoreAs(string database)
    {
        var moves = Files.Select(file =>
            $"MOVE N'{file.Logical}' TO N'{DataFolder}{database}_{file.Logical}{(file.IsLog ? ".ldf" : ".mdf")}'");
        return $"RESTORE DATABASE {SqlServerAdmin.Quote(database)} FROM DISK = N'{Backup}' WITH {string.Join(", ", moves)}, RECOVERY";
    }
}

/// <summary>
/// Limits how many test databases are restored at once, as <c>PostgresDbThrottle</c> does for PostgreSQL.
/// </summary>
internal static class SqlServerDbThrottle
{
    public static readonly SemaphoreSlim DbSlot = new(2, 2);
}

/// <summary>
/// Generic base for SQL Server integration-test contexts shared by all services: each instance restores a
/// database of its own from the per-process template and drops it on dispose.
/// </summary>
public abstract class SqlServerIntegrationTestBase<TService, TDbContext>
    : ServiceDbTestExecutionContext<TService, TDbContext>
    where TService : class
    where TDbContext : DbContext
{
    private readonly string _adminConnStr;
    private readonly string _testDbName;
    private readonly string _testConnStr;
    private int _disposed;
    private bool _slotAcquired;

    protected SqlServerIntegrationTestBase(
        string adminConnStr, string testDbName, string testConnStr,
        Action<IServiceCollection>? configure)
    {
        _adminConnStr = adminConnStr;
        _testDbName = testDbName;
        _testConnStr = testConnStr;

        // Domain events run as in the service once the test registers them, as on the in-memory context.
        Services.AddDbContext<TDbContext>((sp, opts) =>
        {
            opts.UseSqlServer(testConnStr);
            if (sp.GetService<DomainEventPreSaveInterceptor>() != null)
                opts.ApplyDomainEventInterceptors(sp);
        });
        configure?.Invoke(Services);
    }

    /// <summary>
    /// Restores a new test database from the per-process template and returns a ready context; the
    /// template is created and migrated on the first call.
    /// </summary>
    protected static async Task<TContext> CreateCoreAsync<TContext>(
        string adminConnStr,
        string dbPrefix,
        Func<string, string, string, Action<IServiceCollection>?, TContext> factory,
        Action<IServiceCollection>? configure)
        where TContext : SqlServerIntegrationTestBase<TService, TDbContext>
    {
        // The template is made outside the throttle: once per process, and waiting inside it would deadlock
        // the tests that wait for the first one to finish its migrations.
        var template = await SqlServerTemplateManager.GetOrCreateAsync(adminConnStr, dbPrefix, MigrateAsync);

        if (!await SqlServerDbThrottle.DbSlot.WaitAsync(TimeSpan.FromSeconds(120)))
            throw new TimeoutException("SqlServerDbThrottle: no database slot after 120 seconds; a test likely hung.");

        var testDbName = $"{dbPrefix}_{Guid.NewGuid():N}";
        TContext ctx;
        try
        {
            await using (var connection = await SqlServerAdmin.OpenAsync(adminConnStr))
                await SqlServerAdmin.ExecuteAsync(connection, template.RestoreAs(testDbName));

            ctx = factory(adminConnStr, testDbName, SqlServerAdmin.BuildDbConnStr(adminConnStr, testDbName, maxPoolSize: 5), configure);
        }
        catch
        {
            SqlServerDbThrottle.DbSlot.Release();
            await TryDropDatabaseAsync(adminConnStr, testDbName);
            throw;
        }

        // From here on the context owns the slot: its DisposeAsync releases it.
        ctx._slotAcquired = true;
        return ctx;
    }

    // The template already has the migrations - nothing to do per test.
    public override Task EnsureDatabaseCreatedAsync() => Task.CompletedTask;

    public override Task EnsureDatabaseDeletedAsync() => Task.CompletedTask;

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            try
            {
                using var pooled = new SqlConnection(_testConnStr);
                SqlConnection.ClearPool(pooled);
            }
            catch
            {
                // Clearing the pool is a best-effort cleanup.
            }

            await TryDropDatabaseAsync(_adminConnStr, _testDbName);
        }
        finally
        {
            if (_slotAcquired)
            {
                try { SqlServerDbThrottle.DbSlot.Release(); }
                catch (SemaphoreFullException) { /* already released */ }
            }

            await base.DisposeAsync();
        }
    }

    private static async Task TryDropDatabaseAsync(string adminConnStr, string dbName)
    {
        try
        {
            await using var connection = await SqlServerAdmin.OpenAsync(adminConnStr);
            await SqlServerAdmin.DropDatabaseAsync(connection, dbName);
        }
        catch (Exception ex)
        {
            // A drop that fails does not fail the test, but it is written to the test output.
            Console.Error.WriteLine($"Could not drop test database {dbName}: {ex.Message}");
        }
    }

    // Applies the migrations to the template database, once per process.
    private static async Task MigrateAsync(string connStr)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TDbContext>(opts => opts.UseSqlServer(connStr));
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await db.Database.MigrateAsync();
    }
}
