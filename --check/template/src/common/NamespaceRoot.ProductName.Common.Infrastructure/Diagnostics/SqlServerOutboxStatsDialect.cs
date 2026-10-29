using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Diagnostics;

internal sealed class SqlServerOutboxStatsDialect : IOutboxStatsDialect
{
    public string TotalsSql(string outboxTable) => $@"
                SELECT
                    CAST(COALESCE(SUM(CASE WHEN [SentTime] <= '1900-01-01' THEN 1 ELSE 0 END), 0) AS bigint) AS [Pending],
                    CAST(COALESCE(SUM(CASE WHEN [SentTime] >  '1900-01-01' THEN 1 ELSE 0 END), 0) AS bigint) AS [Sent],
                    MIN(CASE WHEN [SentTime] <= '1900-01-01' THEN [EnqueueTime] END) AS [OldestPendingAt],
                    MAX(CASE WHEN [SentTime] >  '1900-01-01' THEN [SentTime] END)    AS [LastSentAt]
                FROM {outboxTable}";

    public string SampleSql(string outboxTable) => $@"
            SELECT TOP (@sampleSize)
                   CAST([MessageId] AS nvarchar(36)) AS [MessageId],
                   [EnqueueTime]                     AS [EnqueueTime],
                   CASE WHEN [SentTime] <= '1900-01-01' THEN NULL ELSE [SentTime] END AS [SentTime]
            FROM {outboxTable}
            WHERE (@filter IS NULL OR [Body] LIKE '%' + @filter + '%')
            ORDER BY [EnqueueTime] DESC";

    public DbParameter[] SampleParameters(string? filter, int sampleSize) =>
    [
        new SqlParameter("@filter", SqlDbType.NVarChar, -1) { Value = (object?)filter ?? DBNull.Value },
        new SqlParameter("@sampleSize", SqlDbType.Int) { Value = sampleSize },
    ];
}
