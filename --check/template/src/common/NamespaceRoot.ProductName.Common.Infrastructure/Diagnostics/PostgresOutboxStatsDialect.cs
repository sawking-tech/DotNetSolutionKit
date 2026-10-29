using System.Data.Common;
using Npgsql;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Diagnostics;

internal sealed class PostgresOutboxStatsDialect : IOutboxStatsDialect
{
    public string TotalsSql(string outboxTable) => $@"
                SELECT
                    COUNT(*) FILTER (WHERE ""SentTime"" <= TIMESTAMPTZ '1900-01-01')::bigint AS ""Pending"",
                    COUNT(*) FILTER (WHERE ""SentTime"" >  TIMESTAMPTZ '1900-01-01')::bigint AS ""Sent"",
                    MIN(""EnqueueTime"") FILTER (WHERE ""SentTime"" <= TIMESTAMPTZ '1900-01-01') AS ""OldestPendingAt"",
                    MAX(""SentTime"")    FILTER (WHERE ""SentTime"" >  TIMESTAMPTZ '1900-01-01') AS ""LastSentAt""
                FROM {outboxTable}";

    public string SampleSql(string outboxTable) => $@"
            SELECT ""MessageId""::text AS ""MessageId"",
                   ""EnqueueTime""     AS ""EnqueueTime"",
                   CASE WHEN ""SentTime"" <= TIMESTAMPTZ '1900-01-01' THEN NULL ELSE ""SentTime"" END AS ""SentTime""
            FROM {outboxTable}
            WHERE (@filter IS NULL OR ""Body"" LIKE '%' || @filter || '%')
            ORDER BY ""EnqueueTime"" DESC
            LIMIT @sampleSize";

    // Explicit NpgsqlDbType - without it the provider can't infer the type of @filter when the value is
    // DBNull, raising `42P08: could not determine data type`.
    public DbParameter[] SampleParameters(string? filter, int sampleSize) =>
    [
        new NpgsqlParameter("filter", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)filter ?? DBNull.Value },
        new NpgsqlParameter("sampleSize", NpgsqlTypes.NpgsqlDbType.Integer) { Value = sampleSize },
    ];
}
