// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using Npgsql;

namespace NamespaceRoot.ProductName.Common.Tests;

/// <summary>
/// Low-level Postgres admin operations used by template management and per-test DB lifecycle.
/// All methods use one statement per command — DROP/CREATE DATABASE cannot run in a pipeline (PG 25001).
/// </summary>
internal static class PostgresAdmin
{
    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public static Task TerminateConnectionsAsync(NpgsqlConnection conn, string dbName) =>
        ExecuteAsync(conn,
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}' AND pid <> pg_backend_pid()");

    public static Task SetTemplateFlagAsync(NpgsqlConnection conn, string dbName, bool isTemplate) =>
        ExecuteAsync(conn,
            $"UPDATE pg_database SET datistemplate={(isTemplate ? "true" : "false")} WHERE datname='{dbName}'");

    public static Task CreateDatabaseAsync(NpgsqlConnection conn, string dbName) =>
        ExecuteAsync(conn, $"CREATE DATABASE \"{dbName}\"");

    public static Task CloneDatabaseAsync(NpgsqlConnection conn, string dbName, string templateName) =>
        ExecuteAsync(conn, $"CREATE DATABASE \"{dbName}\" TEMPLATE \"{templateName}\"");

    public static Task DropDatabaseAsync(NpgsqlConnection conn, string dbName) =>
        ExecuteAsync(conn, $"DROP DATABASE IF EXISTS \"{dbName}\"");

    public static async Task<NpgsqlConnection> OpenAsync(string connStr)
    {
        var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// Builds an admin connection string with short timeouts — used for DROP operations so they
    /// fail fast instead of hanging on a busy/dying server.
    /// </summary>
    public static string BuildShortTimeoutAdminConnStr(string adminConnStr) =>
        // No ConnectionIdleLifetime here: below the pruning interval (10 s by default) Npgsql refuses the
        // connection string, and every DROP failed before it started.
        new NpgsqlConnectionStringBuilder(adminConnStr)
        {
            CommandTimeout         = 15,
            Timeout                = 15
        }.ToString();

    /// <summary>
    /// Builds a connection string targeting a specific database, with sane pool/timeout defaults.
    /// </summary>
    public static string BuildDbConnStr(string adminConnStr, string dbName, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(adminConnStr)
        {
            Database    = dbName,
            MaxPoolSize = maxPoolSize,
            Timeout     = 60
        }.ToString();
}

/// <summary>
/// Manages a single per-process template database for each (adminConnStr, dbPrefix) pair.
/// CREATE DATABASE ... TEMPLATE copies PG data files without executing DDL — 10–50× faster
/// than running EF migrations on every test.
/// </summary>
public static class PostgresTemplateManager
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Templates = new();

    public static async Task<string> GetOrCreateAsync(
        string adminConnStr,
        string dbPrefix,
        Func<string, Task> migrate)
    {
        var key = $"{adminConnStr}||{dbPrefix}";
        var lazy = Templates.GetOrAdd(key,
            _ => new Lazy<Task<string>>(
                () => CreateTemplateAsync(adminConnStr, dbPrefix, migrate),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value;
        }
        catch
        {
            // Don't cache failures — let the next test retry template creation.
            Templates.TryRemove(key, out _);
            throw;
        }
    }

    private static async Task<string> CreateTemplateAsync(
        string adminConnStr, string dbPrefix, Func<string, Task> migrate)
    {
        var name = $"{dbPrefix}_tmpl_{Environment.ProcessId}";

        await using var conn = await PostgresAdmin.OpenAsync(adminConnStr);

        await DropStaleTemplateAsync(conn, name);
        await PostgresAdmin.CreateDatabaseAsync(conn, name);
        await ApplyMigrationsAsync(adminConnStr, name, migrate);
        await PostgresAdmin.TerminateConnectionsAsync(conn, name);
        await PostgresAdmin.SetTemplateFlagAsync(conn, name, isTemplate: true);

        return name;
    }

    private static async Task DropStaleTemplateAsync(NpgsqlConnection conn, string name)
    {
        await PostgresAdmin.SetTemplateFlagAsync(conn, name, isTemplate: false);
        await PostgresAdmin.TerminateConnectionsAsync(conn, name);
        await PostgresAdmin.DropDatabaseAsync(conn, name);
    }

    private static async Task ApplyMigrationsAsync(
        string adminConnStr, string dbName, Func<string, Task> migrate)
    {
        // Larger pool than per-test DBs: EF migrator opens Exists check + advisory lock +
        // migration application = up to ~3 concurrent connections.
        var migrationConnStr = PostgresAdmin.BuildDbConnStr(adminConnStr, dbName, maxPoolSize: 10);

        try
        {
            await migrate(migrationConnStr);
        }
        finally
        {
            // CRITICAL: Npgsql caches the pool by connection string. If we don't clear it,
            // idle connections to the template DB remain, preventing CREATE DATABASE ... TEMPLATE
            // from using it (PG requires zero active connections to the source template).
            await using var poolConn = new NpgsqlConnection(migrationConnStr);
            NpgsqlConnection.ClearPool(poolConn);
        }
    }
}

/// <summary>
/// Shared slot throttle — limits concurrent CREATE DATABASE calls to avoid overwhelming the
/// PG global lock on pg_database and the WAL writer on slow-disk CI environments.
/// Must live in a non-generic class — a static field on a generic type is per-type-instantiation
/// in .NET, so each TService would get its own semaphore, defeating the purpose.
/// </summary>
public static class PostgresDbThrottle
{
    // Postgres serializes CREATE DATABASE internally (global lock on pg_database), so running
    // more than ~2 in parallel gives no throughput benefit and only adds contention.
    // Worst case connections per slot: MaxPoolSize=5 + 1 admin = 6.
    // Rule: assemblies × slots × 6 < max_connections.
    public static readonly SemaphoreSlim DbSlot = new(2, 2);
}

/// <summary>
/// Generic base for PostgreSQL integration-test contexts shared by all services.
/// Each instance creates a dedicated database cloned from a shared per-process template,
/// then drops the database on dispose — full per-test isolation at file-copy speed.
/// </summary>
public abstract class PostgresIntegrationTestBase<TService, TDbContext>
    : ServiceDbTestExecutionContext<TService, TDbContext>
    where TService : class
    where TDbContext : DbContext
{
    private readonly string _adminConnStr;
    private readonly string _testDbName;
    private readonly string _testConnStr;

    // Guards against double-Release of the semaphore on repeated DisposeAsync calls.
    private int _disposed;
    // True only when this instance successfully acquired a slot (so we know to release it).
    private bool _slotAcquired;

    protected PostgresIntegrationTestBase(
        string adminConnStr, string testDbName, string testConnStr,
        Action<IServiceCollection>? configure)
    {
        _adminConnStr = adminConnStr;
        _testDbName   = testDbName;
        _testConnStr  = testConnStr;

        // Domain events run as in the service once the test registers them, as on the in-memory context.
        Services.AddDbContext<TDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(testConnStr);
            if (sp.GetService<DomainEventPreSaveInterceptor>() != null)
                opts.ApplyDomainEventInterceptors(sp);
        });
        configure?.Invoke(Services);
    }

    /// <summary>
    /// Creates a new test database cloned from the per-process template and returns a ready context.
    /// The template is created with migrations applied on the first call; subsequent calls use
    /// <c>CREATE DATABASE … TEMPLATE</c> which copies PG data files without any DDL.
    /// </summary>
    protected static async Task<TContext> CreateCoreAsync<TContext>(
        string adminConnStr,
        string dbPrefix,
        Func<string, string, string, Action<IServiceCollection>?, TContext> factory,
        Action<IServiceCollection>? configure)
        where TContext : PostgresIntegrationTestBase<TService, TDbContext>
    {
        // Template creation runs OUTSIDE the throttle: it happens at most once per process
        // per service, and blocking inside the semaphore would deadlock if all slots are
        // taken by tests waiting for the first one to finish migrations.
        var templateName = await PostgresTemplateManager.GetOrCreateAsync(
            adminConnStr, dbPrefix, MigrateAsync);

        await AcquireSlotAsync();

        var testDbName = $"{dbPrefix}_{Guid.NewGuid():N}";

        try
        {
            await CloneFromTemplateAsync(adminConnStr, testDbName, templateName);
        }
        catch
        {
            PostgresDbThrottle.DbSlot.Release();
            throw;
        }

        var testConnStr = PostgresAdmin.BuildDbConnStr(adminConnStr, testDbName, maxPoolSize: 5);

        return await BuildContextAsync(adminConnStr, testDbName, testConnStr, factory, configure);
    }

    private static async Task AcquireSlotAsync()
    {
        if (!await PostgresDbThrottle.DbSlot.WaitAsync(TimeSpan.FromSeconds(120)))
            throw new TimeoutException(
                "PostgresDbThrottle: no database slot available after 120 seconds. " +
                "A test likely hung and did not release its slot.");
    }

    private static async Task CloneFromTemplateAsync(
        string adminConnStr, string testDbName, string templateName)
    {
        await using var conn = await PostgresAdmin.OpenAsync(adminConnStr);
        await PostgresAdmin.CloneDatabaseAsync(conn, testDbName, templateName);
    }

    private static async Task<TContext> BuildContextAsync<TContext>(
        string adminConnStr,
        string testDbName,
        string testConnStr,
        Func<string, string, string, Action<IServiceCollection>?, TContext> factory,
        Action<IServiceCollection>? configure)
        where TContext : PostgresIntegrationTestBase<TService, TDbContext>
    {
        TContext ctx;
        try
        {
            ctx = factory(adminConnStr, testDbName, testConnStr, configure);
        }
        catch
        {
            PostgresDbThrottle.DbSlot.Release();
            await TryDropDatabaseAsync(adminConnStr, testDbName);
            throw;
        }

        // From here on, the ctx OWNS the slot — its DisposeAsync will release it.
        ctx._slotAcquired = true;

        try
        {
            await ctx.EnsureDatabaseCreatedAsync();
            return ctx;
        }
        catch
        {
            await ctx.DisposeAsync(); // releases the slot + drops the DB
            throw;
        }
    }

    // Template already has migrations applied — nothing to do per test.
    public override Task EnsureDatabaseCreatedAsync() => Task.CompletedTask;

    public override Task EnsureDatabaseDeletedAsync() => Task.CompletedTask;

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try
        {
            // Drop FIRST while the slot is still held — this prevents another test from
            // starting CREATE DATABASE ... TEMPLATE while we still have connections to clean up.
            await DropTestDatabaseAsync();
        }
        finally
        {
            ReleaseSlot();
            await base.DisposeAsync();
        }
    }

    private void ReleaseSlot()
    {
        if (!_slotAcquired) return;
        try { PostgresDbThrottle.DbSlot.Release(); }
        catch (SemaphoreFullException) { /* defensive: already released */ }
    }

    private async Task DropTestDatabaseAsync()
    {
        ClearTestConnectionPool();
        await TryDropDatabaseAsync(_adminConnStr, _testDbName);
    }

    private void ClearTestConnectionPool()
    {
        try
        {
            using var poolConn = new NpgsqlConnection(_testConnStr);
            NpgsqlConnection.ClearPool(poolConn);
        }
        catch
        {
            // Non-fatal: clearing the pool is a best-effort cleanup.
        }
    }

    private static async Task TryDropDatabaseAsync(string adminConnStr, string dbName)
    {
        try
        {
            var shortTimeoutConnStr = PostgresAdmin.BuildShortTimeoutAdminConnStr(adminConnStr);
            await using var conn = await PostgresAdmin.OpenAsync(shortTimeoutConnStr);

            await PostgresAdmin.TerminateConnectionsAsync(conn, dbName);
            await PostgresAdmin.DropDatabaseAsync(conn, dbName);
        }
        catch (Exception ex)
        {
            // DROP failure does not fail the test, but it is written to the test output: a silent catch
            // here once hid that no test database was ever dropped.
            Console.Error.WriteLine($"Could not drop test database {dbName}: {ex.Message}");
        }
    }

    // Used by PostgresTemplateManager to apply migrations to the template DB once per process.
    private static async Task MigrateAsync(string connStr)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TDbContext>(opts => opts.UseNpgsql(connStr));
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await db.Database.MigrateAsync();
    }
}