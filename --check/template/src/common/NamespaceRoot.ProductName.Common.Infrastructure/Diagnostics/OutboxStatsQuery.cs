using NamespaceRoot.ProductName.Common.Contracts.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Diagnostics;

/// <summary>
/// Reads the MassTransit EF outbox table of a service: pending and sent counts and a sample of the
/// latest rows.
/// </summary>
/// <remarks>
/// A table name cannot be a SQL parameter, so it goes into the text of the query. It is taken from
/// the EF model of the context, where <c>AddTransactionalOutbox</c> placed it in the service's schema,
/// and quoted by the provider, so nothing a caller passes reaches the SQL text.
/// </remarks>
public static class OutboxStatsQuery
{
    public static async Task<OutboxStatsResponse> RunAsync(
        DbContext db,
        string? filter,
        int sampleSize,
        CancellationToken ct)
    {
        var outboxTable = ResolveOutboxTable(db);
        var dialect = DatabaseProvider.CreateOutboxStatsDialect();

        // Aggregate counters across the whole table - pending vs sent + the age of the oldest pending row
        // + the freshness of the last sent row tells the operator at a glance whether the delivery worker
        // is keeping up or stopped draining.
        var totals = await db.Database
            .SqlQueryRaw<OutboxTotals>(dialect.TotalsSql(outboxTable))
            .SingleAsync(ct);

        // Sample of the most recent rows for hands-on inspection; respects the optional body-substring
        // filter so the caller can zoom in on one publish (e.g. an order id).
        var rows = await db.Database
            .SqlQueryRaw<OutboxRowSnapshot>(dialect.SampleSql(outboxTable),
                dialect.SampleParameters(filter, Math.Clamp(sampleSize, 1, 50)))
            .ToListAsync(ct);

        return new OutboxStatsResponse
        {
            Pending = totals.Pending,
            Sent = totals.Sent,
            OldestPendingAt = totals.OldestPendingAt,
            LastSentAt = totals.LastSentAt,
            Sample = rows,
        };
    }

    /// <summary>The quoted, schema-qualified outbox table of <paramref name="db"/>.</summary>
    internal static string ResolveOutboxTable(DbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(MassTransit.EntityFrameworkCoreIntegration.OutboxMessage))
                     ?? throw new InvalidOperationException(
                         $"{db.GetType().Name} has no outbox: call modelBuilder.AddTransactionalOutbox(schema) in OnModelCreating.");

        var table = entity.GetTableName()
                    ?? throw new InvalidOperationException("The outbox message entity is not mapped to a table.");

        return db.GetService<ISqlGenerationHelper>().DelimitIdentifier(table, entity.GetSchema());
    }

    private sealed record OutboxTotals(long Pending, long Sent, DateTime? OldestPendingAt, DateTime? LastSentAt);
}
