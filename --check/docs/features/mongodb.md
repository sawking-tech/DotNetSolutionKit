# MongoDB

`--MongoDB` adds `Capabilities.Mongo`, a project of its own, with what a service that keeps documents in MongoDB
needs beside its main database, and registers it in the service, which references the project. MongoDB does not replace `--Database`: entities, migrations and
the outbox stay in PostgreSQL or SQL Server. The queries stay in the service: a reader or writer is a
class of the service's infrastructure behind a port of its application layer.

| Part | |
|---|---|
| `IMongoStore` | the service's database and its collections, over one client for the process; 503 when switched off |
| `Guid` | a `Guid` property is stored as the standard UUID subtype, which the driver otherwise refuses to write; `[BsonGuidRepresentation]` on a property chooses another, and a `Guid` inside an `object` or a dictionary of objects goes through the object serializer instead |
| readiness | `/ready` sends `ping` to the service's database while MongoDB is switched on, and gives up after 5 seconds |

```csharp
public sealed class ReceiptStore(IMongoStore mongo) : IReceiptStore
{
    private IMongoCollection<Receipt> Receipts => mongo.Collection<Receipt>("receipts");

    public Task SaveAsync(Receipt receipt, CancellationToken ct) =>
        Receipts.ReplaceOneAsync(r => r.Id == receipt.Id, receipt, new ReplaceOptions { IsUpsert = true }, ct);

    public Task<Receipt?> FindAsync(Guid id, CancellationToken ct) =>
        Receipts.Find(r => r.Id == id).FirstOrDefaultAsync(ct)!;
}
```

The client connects on first use, so a service with MongoDB switched off opens no socket. The service
creates its indexes itself, at startup, through the collection's `Indexes`.

## Settings

| Setting | |
|---|---|
| `MongoDB:Enabled` | `true`; `false` makes the database answer 503, drops the readiness check and the requirement for a connection string and a database |
| `MongoDB:ConnectionString` | `mongodb://user:password@host:27017/?authSource=admin`, from the environment or the secret store; checked at startup |
| `MongoDB:Database` | the service's own database; the generated value is the service's name |

Under docker compose the infrastructure gets a MongoDB server; the credentials come from
`deploy/compose/.env`.

## Tests

A service test does not reach MongoDB: it gives the service an in-memory double of its reader or writer
port, as for any other port. The driver is tested against a real server: `MongoTestDatabase` in
`Capabilities.Mongo.Tests` names a database of the test's own on the server named by `TEST_MONGO` and drops it
afterwards. Without the variable the test is skipped.

```bash
eval "$(bash tests/servers/up.sh)"     # the test servers, MongoDB among them (tests/servers/mongo.sh)
dotnet test --filter "FullyQualifiedName~Mongo"
```

`Capabilities.Mongo.Tests` checks the core this way: a document written reads back as it was, its `Guid` included, and
readiness reports the server; without a server, the settings it requires, a malformed connection string, and
the switch.
