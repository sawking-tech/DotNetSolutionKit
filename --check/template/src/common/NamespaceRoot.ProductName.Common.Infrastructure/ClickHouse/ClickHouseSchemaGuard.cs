using ClickHouse.Client.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;

/// <summary>
/// Checks at startup that a table carries every column an insert names, and refuses to start when it
/// does not.
/// </summary>
/// <remarks>
/// An insert that lists its columns fails outright on a column the table lacks, and a write path that
/// fails quietly - a projection fed by events, for one - stops advancing with nothing visible until
/// someone notices the data has gone stale. Refusing to start turns that into an immediate failure:
/// under blue-green the new version never takes traffic and the running one keeps serving. ClickHouse
/// DDL is not migrated by the service, so the message says what to apply.
/// </remarks>
public sealed class ClickHouseSchemaGuard(
    IClickHouseConnections connections,
    IOptions<ClickHouseOptions> options,
    ILogger<ClickHouseSchemaGuard> logger)
{
    /// <param name="table">The table, qualified with its database (<c>reports.usage</c>) or in <c>default</c>.</param>
    /// <param name="columns">The columns the insert names.</param>
    /// <param name="remedy">What to do when the check fails, e.g. which DDL file to apply.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task EnsureColumnsAsync(
        string table, IReadOnlyCollection<string> columns, string remedy, CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("ClickHouse is switched off: skipping the schema check of {Table}", table);
            return;
        }

        var (database, name) = SplitQualifiedName(table);

        await using var connection = connections.Create();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM system.columns WHERE database = {p_db:String} AND table = {p_table:String}";
        command.CommandTimeout = connections.CommandTimeoutSeconds;
        command.AddParameter("p_db", "String", database);
        command.AddParameter("p_table", "String", name);

        var present = new HashSet<string>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                present.Add(reader.GetString(0));
        }

        var missing = FindMissingColumns(columns, present);
        if (missing.Count == 0)
        {
            logger.LogInformation("ClickHouse schema check passed: {Table} carries all {Count} columns", table, columns.Count);
            return;
        }

        // An empty result means the table itself is absent - a different fault with the same cure, so it
        // is worth saying which of the two happened.
        var problem = present.Count == 0
            ? $"Table {table} does not exist (or is not visible to this user)."
            : $"Table {table} is missing {missing.Count} column(s) the insert names: {string.Join(", ", missing)}.";

        throw new InvalidOperationException($"{problem} {remedy}");
    }

    /// <summary>Columns the insert names that the table does not have, in the order the insert lists them.</summary>
    internal static IReadOnlyList<string> FindMissingColumns(IEnumerable<string> required, ISet<string> present) =>
        required.Where(c => !present.Contains(c)).ToList();

    internal static (string Database, string Table) SplitQualifiedName(string qualified)
    {
        var separator = qualified.IndexOf('.');
        return separator < 0
            ? ("default", qualified)
            : (qualified[..separator], qualified[(separator + 1)..]);
    }
}
