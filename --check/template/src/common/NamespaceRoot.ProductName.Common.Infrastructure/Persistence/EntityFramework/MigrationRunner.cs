using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;

public sealed class MigrationRunner
{
    private MigrationRunner()
    {
    }

    public static void RunMigrations(
        DbContext context,
        ILogger logger,
        Action<DbContext>? preMigrationHook = null)
    {
        var lockName = context.Model.GetDefaultSchema() ?? context.GetType().Name;
        var lockKey = ComputeLockKey(lockName);
        var connectionString = context.Database.GetDbConnection().ConnectionString;

        logger.LogInformation(
            "Acquiring the migration lock. Scope={Scope}, Key={Key}",
            lockName, lockKey);

        var sw = Stopwatch.StartNew();

        var migrationLock = DatabaseProvider.CreateMigrationLock();
        using var lockConnection = migrationLock.Connect(connectionString);

        try
        {
            migrationLock.Acquire(lockConnection, lockKey);

            logger.LogInformation(
                "Migration lock acquired for {Scope} after {ElapsedMs} ms. Checking schema state...",
                lockName, sw.ElapsedMilliseconds);

            preMigrationHook?.Invoke(context);

            // A generated service starts with no migrations, and the start would otherwise look
            // fine: nothing pending, "up to date", and then every query fails on a missing table.
            if (!context.Database.GetMigrations().Any())
            {
                logger.LogCritical(
                    "{Context} has no migrations, so the database has none of its tables. Add the first one: " +
                    "dotnet ef migrations add Initial -p <the Infrastructure project> -s <the Infrastructure project> " +
                    "-o EntityFramework/Migrations",
                    context.GetType().Name);
            }

            var pendingMigrations = context.Database.GetPendingMigrations().ToList();

            if (pendingMigrations.Count > 0)
            {
                logger.LogInformation(
                    "Found {Count} pending migrations: {Migrations}. Applying...",
                    pendingMigrations.Count,
                    string.Join(", ", pendingMigrations));

                context.Database.Migrate();

                logger.LogInformation("Migrations applied successfully.");
            }
            else
            {
                logger.LogInformation("Database is up to date. No migrations to apply.");
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Migration process failed! Root cause: {Message}", ex.Message);
            throw;
        }
        finally
        {
            try
            {
                migrationLock.Release(lockConnection, lockKey);
                logger.LogInformation("Migration lock released for {Scope}.", lockName);
            }
            catch (Exception releaseEx)
            {
                logger.LogError(
                    releaseEx,
                    "Failed to release the migration lock for {Scope}, key {Key}. " +
                    "Lock will be auto-released when the session ends.",
                    lockName, lockKey);
            }
        }
    }

    internal static long ComputeLockKey(string scope)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope));
        return BitConverter.ToInt64(hash, 0);
    }
}
