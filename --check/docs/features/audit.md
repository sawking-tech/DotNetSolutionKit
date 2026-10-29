# Audit journal

Generated with `--Audit` together with `--Messaging outbox`, off by default. Without the outbox the flag
does nothing, and the generation says so: an entry has to commit with the change it describes, and only
the outbox writes a message in the same transaction. Pass both with `--Solution`, and again to each service
generated later.

## What is recorded

An entity marked `[Auditable]` publishes `AuditRecordedV1` for every created, updated or deleted row:

```csharp
[Auditable("Orders", DisplayProperty = nameof(Number), SubjectTenantProperty = nameof(TenantId))]
public class Order : AggregateRoot<Guid>
{
    public string Number { get; private set; }
    public Guid TenantId { get; private set; }

    [AuditRedact] public string ReturnSecret { get; private set; }   // the change, not the value
    [AuditIgnore] public int SyncAttempts { get; private set; }      // moves without meaning
}
```

The entry carries the module, the entity, its id and display value, the action, the diff
`{"Status":{"old":"Draft","new":"Placed"}}`, who acted and for which tenant, the tenant the row belongs
to, the service, the trace and the [correlation identifier](../architecture/web-layer.md#correlation).

- The interceptor publishes inside `SaveChanges`, into the outbox through the same context: the entry
  commits with the change, and a rolled back change leaves none.
- A creation records every value it sets, `false` included; an update records what moved; a deletion
  records the action alone. `CreatedAt`, `UpdatedAt`, the key and concurrency tokens are left out.
- A value object owned by the entity is folded into its diff, as `DeliveryAddress.City`; replacing it
  whole still reports the previous values.
- A job or a consumer with no user is recorded as the system, `Guid.Empty`; an actor that cannot be read
  never fails the save.

## Writes the change tracker does not see

`ExecuteUpdateAsync`, `ExecuteDeleteAsync` and raw SQL bypass the interceptor. Where such a write carries
an operator's decision, record it explicitly:

- `IAuditRecorder.RecordAsync` for one change described by hand;
- `ISetBasedAuditCapture.BeginAsync(query, module)` before the statement and `CompleteAsync()` after it,
  in the same transaction: it reads the affected rows on both sides and publishes `AuditBulkRecordedV1`,
  turned into per-row entries by the consumer. Above 5 000 rows it publishes a count instead of the rows.

## The guards

A generated service runs `AuditGuardTests`:

| Guard | Fails when |
|---|---|
| every entity has a decision | an entity carries neither `[Auditable]` nor `[AuditIgnore]` |
| markers name real properties | `DisplayProperty` or `SubjectTenantProperty` names a missing property, or the subject is not a `Guid` |
| no secret in the journal | a property of an audited entity named like a password, token, key, hash or salt has neither `[AuditRedact]` nor `[AuditIgnore]` |
| set-based writes are declared | a method in a `*Repository.cs` calls `ExecuteUpdateAsync`, `ExecuteDeleteAsync` or raw SQL without `[SetBasedWrite(audited, reason)]` |

The last one reads the source files: a call inside a method body is not visible to reflection.

## Keeping the journal

The template publishes; it does not store. The service that keeps the journal consumes both messages
into a table of its own, and decides who may read what, for example by `ActorTenantId` and
`SubjectTenantId`:

```csharp
public sealed class AuditRecordedConsumer(JournalDbContext db) : IConsumer<AuditRecordedV1>
{
    public async Task Consume(ConsumeContext<AuditRecordedV1> context)
    {
        db.Entries.Add(JournalEntry.From(context.Message));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
```

## Tests

`Common.Tests` covers the interceptor (creation, update, deletion, owned values, collections, dates,
redaction, the subject tenant, the system actor, the correlation identifier) and the set-based capture.
Checked live under compose: a created order published one `AuditRecordedV1` carrying the request's
correlation identifier, and a rolled back one published nothing.
