using System.Data.Common;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Diagnostics;

/// <summary>
/// The SQL of <see cref="OutboxStatsQuery"/> in one database's dialect: the totals of the outbox table and a
/// sample of its latest rows.
/// </summary>
/// <remarks>
/// MassTransit's EF migration makes "SentTime" NOT NULL and writes the .NET DateTime.MinValue sentinel
/// (0001-01-01) for rows that haven't been delivered yet, so a "pending" row is one with "SentTime" at or
/// before 1900-01-01, in every dialect.
/// </remarks>
internal interface IOutboxStatsDialect
{
    /// <summary>Pending, Sent, OldestPendingAt and LastSentAt of <paramref name="outboxTable"/>, one row.</summary>
    string TotalsSql(string outboxTable);

    /// <summary>MessageId, EnqueueTime and SentTime of the latest rows, filtered by @filter, at most @sampleSize.</summary>
    string SampleSql(string outboxTable);

    /// <summary>The @filter (null for none) and @sampleSize parameters of <see cref="SampleSql"/>.</summary>
    DbParameter[] SampleParameters(string? filter, int sampleSize);
}
