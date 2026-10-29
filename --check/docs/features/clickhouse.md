# ClickHouse

`--ClickHouse` adds to `Common` what every service that reads or writes ClickHouse needs, and registers it
in the service. The queries stay in the service: a reader or writer is a class of the service's
infrastructure behind a port of its application layer.

| Part | |
|---|---|
| `IClickHouseConnections` | connections over one shared HTTP client, with the response decompression ClickHouse needs; 503 when switched off |
| `ClickHouseSchemaGuard` | at startup, refuses to start when a table lacks a column an insert names, and says what to apply |
| `ClickHouseValues` | one coercion of the driver's weakly typed values, and the pagination parameters of a paged query |
| readiness | `/ready` checks `SELECT 1` while ClickHouse is switched on |

```csharp
public sealed class UsageReader(IClickHouseConnections connections) : IUsageReader
{
    public async Task<IReadOnlyList<Usage>> PageAsync(int page, int size, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, amount FROM reports.usage ORDER BY id LIMIT {p_page_size:UInt32} OFFSET {p_offset:UInt64}";
        ClickHouseValues.ApplyPagination(command, page, size);
        ...
    }
}
```

## The schema check

ClickHouse DDL is not run by the service's migrations. An insert that lists its columns fails on a
column the table lacks, and a write path fed by events stops advancing without anything visible. A
service calls the guard at startup for each table it writes:

```csharp
await guard.EnsureColumnsAsync("reports.usage", UsageWriter.Columns, "Apply deploy/clickhouse/002-usage-amount.sql.");
```

A missing table and a missing column are told apart in the message. Under blue-green the new version
then never takes traffic, and the running one keeps serving.

## Settings

| Setting | |
|---|---|
| `ClickHouse:Enabled` | `true`; `false` makes a connection answer 503, drops the readiness check and the requirement for a connection string |
| `ClickHouse:ConnectionString` | `Host=...;Port=8123;Username=...;Password=...;Database=...`, from the environment or the secret store |
| `ClickHouse:CommandTimeoutSeconds` | 30 |
| `ClickHouse:MaxInsertBlockRows` | 10 000: the largest insert sent as one block, and so the largest that server-side deduplication covers |

Under docker compose the infrastructure gets a ClickHouse server; the credentials come from
`deploy/compose/.env`.

## Tests

A service test does not reach ClickHouse: it gives the service an in-memory double of its reader or
writer port, as for any other port. The SQL is tested against a real server: `ClickHouseTestDatabase` in
`Common.Tests` creates a database of the test's own on the server named by `TEST_CLICKHOUSE` and drops it
afterwards; the test creates its tables with `ExecuteAsync`. Without the variable the test is skipped.

```bash
eval "$(bash tests/servers/up.sh)"     # the test servers, ClickHouse among them (tests/servers/clickhouse.sh)
dotnet test --filter "FullyQualifiedName~ClickHouse"
```

`Common.Tests` checks the core this way: a table that fits passes, a missing column and a missing table
stop the start with the right message, a paged read comes back typed, readiness, and the switch.
