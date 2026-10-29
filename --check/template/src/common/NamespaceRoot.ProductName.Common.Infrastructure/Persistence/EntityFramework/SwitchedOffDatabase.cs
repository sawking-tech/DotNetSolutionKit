using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// The database of a service started with <c>Database:Enabled=false</c>.
/// </summary>
/// <remarks>
/// The context stays registered, so everything that depends on it - repositories, the unit of work,
/// handlers - is still built and the start does not fail on a missing registration. Any attempt to open
/// a connection fails before it reaches the network with a <see cref="ServiceUnavailableException"/>,
/// which a client receives as 503 with the setting that explains it.
/// </remarks>
public static class SwitchedOffDatabase
{
    /// <summary>
    /// A connection string that names no reachable host (<c>.invalid</c> is reserved, RFC 2606), in case
    /// something opens a connection around the interceptor.
    /// </summary>
    // Server and Database are keywords of Npgsql and of SqlClient alike, so either provider reads it.
    public const string ConnectionString = "Server=database-switched-off.invalid;Database=none";

    /// <summary>Makes every connection of this context fail with 503.</summary>
    public static DbContextOptionsBuilder UseSwitchedOffDatabase(this DbContextOptionsBuilder options) =>
        options.AddInterceptors(new RefuseConnections());

    private sealed class RefuseConnections : DbConnectionInterceptor
    {
        private static ServiceUnavailableException SwitchedOff() =>
            new($"The database of this service is switched off ({DependencySwitches.DatabaseKey}=false).");

        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw SwitchedOff();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw SwitchedOff();
    }
}
