# Framework.Audit

The audit journal: a change to an entity marked `[Auditable]` leaves a record of itself, and the record commits
with the change or not at all.

## What it holds

| Part | What it does |
|---|---|
| `AuditSaveChangesInterceptor` | Reads the change tracker inside `SaveChanges` and publishes `AuditRecordedV1` into the outbox in the same transaction |
| `AuditRecorder` | Records a change described by hand, for what bypasses the change tracker |
| `SetBasedAuditCapture` | Reads the affected rows on both sides of a statement that bypasses the tracker, and publishes `AuditBulkRecordedV1` |
| `AuditRedaction`, `AuditValueFormatter` | What a record carries: which properties are hidden, how a value is written |
| `AuditActorReader`, `AuditInfrastructureResolver` | The actor of the change, and the scope a pooled context reads it from |
| `AuditPersistenceExtensions` | `AddAuditRecorder` and `ApplyAuditInterceptor`, called from a service's `DbContext` setup |
| `IAuditRecorder`, `ISetBasedAuditCapture` | The ports a repository or a use case calls |
| `AuditMarkerVerifier`, `AuditRedactionVerifier`, `SetBasedWriteVerifier` | The tests of a service hold its entities to their markers with these |
| `NoOpAuditRecorder`, `NoOpAuditCapture` | Record nothing, for a suite whose subject is not the journal |

## What stays in the kernel

`[Auditable]`, `[AuditIgnore]`, `[AuditRedact]` and `[SetBasedWrite]` are in `Common`: the team sets them on its
own entities, and the kernel does not know this module. `AuditRecordedV1` and `AuditBulkRecordedV1` are in
`Common.Contracts`: they cross a service's boundary. The journal a consumer writes them into is the team's.

## On and off

The module comes with `--audit`, and only with `--messaging rabbitmq:outbox`: an entry has to commit with the
change it describes, and only the outbox writes it in the same transaction. A service without the flag carries
neither this code nor its packages. `IAuditRecorder` and `ISetBasedAuditCapture` are its ports, so a service that
uses them without the flag does not compile - which is the flag's job to prevent, not a fallback to hide.

The verifiers are for tests, and `Shouldly` is a private asset: it reaches the test project that references this
module, never the service's output.
