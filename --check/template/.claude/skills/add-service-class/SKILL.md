---
name: add-service-class
description: Rules for an application service (use case) of this solution - one job per method, transactions, idempotent commands, cancellation, naming, money, no N+1.
---

Rules for implementing a service class.

## Usage
`/add-service-class <ClassName>` - e.g. `/add-service-class InvoiceService`

---

## A method does one thing

**validate -> load -> check the policy -> change -> save**

| Layer | Responsibility |
|-------|---------------|
| Service | Orchestrate: load aggregates, call the policy, change them, save |
| Policy | Access: who may do what with this data |
| Repository | Data access only |
| Domain event handler | One side effect per handler |
| Adapter | Translate between the domain and an external system |

**Signs a service does too much:** a constructor with 6+ dependencies; a method body over about 30 lines;
`BackgroundJob.Enqueue` or `bus.PublishAsync` called inline - move them to a domain event handler.

## List methods - all three parts

1. **A filter record**, `sealed`, with the request interfaces it needs (`Common/Domain/Querying`):
   `IPaginationRequest` for a paged list, `ISortableRequest` when it sorts, `ISearchableRequest` when it
   searches.
2. **Specifications and a filter extension**: filtering is business logic, see `/add-specification`.
3. **The repository runs the query**: `ListPageAsync(query, filter, ct)`, see `/add-repository`.

The call site is one line:

```csharp
var page = await orders.ListPageAsync(filter.ToQuery(customerId, search), filter, ct);
return page.ToResponse();
```

## Transactions - every change

**Every change, even creating one entity, is inside `BeginTransactionAsync` / `CommitTransactionAsync`.**

Entity methods raise domain events, and the pre-save handlers run inside `SaveChanges`: they write outbox
rows and may change other aggregates. Without an explicit transaction the entity and the outbox row can
land in separate implicit transactions:

```
SaveChanges (the entity)      -> OK
SaveChanges (the outbox row)  -> FAIL  ->  the entity is saved, the event is lost
```

With one transaction they commit together or not at all, and post-commit handlers run only after the
commit succeeded.

`CommitTransactionAsync` calls `SaveChangesAsync` itself; do not call it before the commit.

```csharp
await unitOfWork.BeginTransactionAsync(ct);
try
{
    order.Cancel(context, request.Reason);        // the change inside the transaction
    await unitOfWork.CommitTransactionAsync(ct);   // save + commit
}
catch
{
    await unitOfWork.RollbackTransactionAsync(ct);
    logger.LogError("Failed to cancel {EntityType} {EntityId}", nameof(Order), order.Id);
    throw;
}
```

Inject `ILogger<TService>` and log the entity type and id in the `catch`: the shared handler records the
exception, the service adds which entity it was about.

## Commands a client may repeat - idempotency

A create or a change a client may retry (a dropped connection, a redelivered message) carries a key the
client chose, and `IIdempotentExecutor` runs the work once per key:

```csharp
public sealed record PlaceOrder(Guid CustomerId, IReadOnlyList<OrderLineInput> Lines, string IdempotencyKey)
    : IIdempotentRequest;

public Task<PlaceOrderResponse> PlaceAsync(PlaceOrder request, CancellationToken ct = default) =>
    idempotent.ExecuteAsync(request, "orders.place", async token =>
    {
        var number = await shortIds.GetNextAsync("orders.order_number_seq", token);
        var order = new Order(context, number, request.CustomerId);
        orders.Add(order);
        return order.ToPlaceResponse();
    }, ct);
```

- The first request does the work and records its key and answer in the same transaction; a repeat gets
  that answer and does nothing. The executor opens the transaction: do not open another inside the work.
- A key used for another operation answers 409 `IDEMPOTENCY_KEY_REUSED`; a key outside 16..128 characters
  is refused before anything runs.
- A duplicate the work itself refuses (a taken name) reaches the caller as that conflict.
- `ExecuteOnceAsync` is for an answer that must not be stored, such as a secret shown once.
- The service needs the log once: `modelBuilder.AddIdempotencyLog()` and a migration,
  `services.AddIdempotency<TDbContext>()` in Infrastructure; both come from
  `NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Idempotency`.

Details: [persistence](https://dotskit.sawking.tech/docs.html#persistence). Notifications and confirmations
that are side effects only, and reads, take no key.

## Async and cancellation - mandatory

- A public method doing I/O (database, bus, HTTP, files) is `async` and returns `Task` / `Task<T>`. No
  `.Result`, no `.Wait()`.
- The last parameter is `CancellationToken ct = default`. Pass it to **every** awaitable: repositories,
  `CommitTransactionAsync(ct)`, `bus.PublishAsync(message, ct)`, HTTP calls, `Task.WhenAll`.
- Never swallow `OperationCanceledException`; catch it only for a domain reason, such as compensation,
  and rethrow.
- Pure in-memory helpers stay synchronous.
- A stream returns `IAsyncEnumerable<T>` with `[EnumeratorCancellation] CancellationToken ct`.

## Naming - Get, Find, Set, Update, Apply, Try, Map, Ensure

| Verb | For | Examples |
|---|---|---|
| `Get…` | A read without side effects that returns or throws | `GetByIdAsync` |
| `Find…` / `TryGet…` | A read that may return null instead of throwing | `FindByNumberAsync` |
| `Set…` | A setter that always succeeds | `SetAsDefault` |
| `Update…` | A change of several fields of an aggregate | `UpdateShippingAddress` |
| `Apply…` | Applying a computed change, often in a batch | `ApplyDiscountsAsync` |
| `Try…` | A boolean attempt that may legitimately fail | `TryReserve` |
| `Try…Async` | The async attempt: returns an outcome record, since `out` cannot cross `await` | `TryReserveStockAsync -> Task<TryReserveStockOutcome>` |
| `Map…` / `To…` | A pure conversion: domain to DTO, event or response | `ToResponse`, `MapListItem` |
| `Resolve…` | A lookup that applies business logic to pick the value | `ResolvePriceAsync` |
| `Ensure…` | An idempotent post-condition, a no-op when it already holds | `EnsureCanCancel` |
| `Create…` | A factory of a new aggregate, not a DTO mapper | `CreateAsync(CreateInvoice)` |
| `Build…` | Only a real step-by-step builder; never a mapper | |

Avoid `Make…`, `Generate…`, `Process…`, `Do…`, and `Handle…` outside event handlers: they hide intent. If no
verb from the table fits, the method has more than one job. A rename crosses tests and skills: it is a
refactoring of its own, never a drive-by.

The async outcome record:

```csharp
public sealed record TryReserveStockOutcome(bool Reserved, Guid? ReservationId);

if (await TryReserveStockAsync(order, ct) is not { Reserved: true, ReservationId: var reservationId })
    return;
```

For a one-bit outcome, `Task<bool>`.

## Tests ship with the method

A public method added or rewritten in a commit ships with its tests in that commit: `/add-tests`.

## Pick the pattern first

Before a method with non-trivial structure (a state machine, a plug-in point, a cascade across services,
deduplication, rate limiting, a projection) run `/pick-pattern`. If it answers "none", write it plainly.

## Compact methods

- A public method body of about 30 lines at most.
- Extract a private method only when it is called from two places, or when the public method exceeds the
  budget and the step's name says what it does.
- No ladder of one-line private helpers, no wrapper around a single repository call.
- Group classes that change for the same reason in one folder, not by suffix.

## No N+1

- A `foreach` that queries inside the loop is N+1: one bulk read (a specification over a set of ids), then
  group in memory.
- A navigation used in a loop is loaded by the query (`.Include`).
- When only a few fields are needed, project them with `Select` before loading.
- One save per request, not one per item.

## Concurrency - choose deliberately

| Pattern | When |
|---|---|
| Sequential `await` | Default |
| `Task.WhenAll` | Independent I/O; bound the fan-out |
| `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` | CPU-bound work over a list, with a cap and the request's token |
| Channel / `IAsyncEnumerable` | A stream with backpressure |
| A Hangfire job | Work that must not block the request |
| Consumer concurrency | Set at the consumer registration, never in the service |

Never shared mutable state without a lock, never `lock` around `await` (use `SemaphoreSlim`), never
`Task.Run` from a request scope. `DbContext` is not thread-safe: a parallel branch gets a scope of its own.

## Phases: an external call and a write

```csharp
// Phase 1 - the external call, outside the transaction; it may partly fail
var results = await carrier.CreateShipmentsAsync(orders, ct);

// Phase 2 - the write, only for what succeeded
await unitOfWork.BeginTransactionAsync(ct);
// apply results.Succeeded
await unitOfWork.CommitTransactionAsync(ct);
```

A batch that can partly fail answers 207 with a result per item, so the client retries only the failed
ones.

## Depend on abstractions

- `IUnitOfWork`, not the `DbContext`; a port, not its implementation; `IMessageBus`, not MassTransit.
- Implementations are registered in the service's `DependencyInjection.cs`, never `new`-ed in application
  code.
- An adapter takes and returns domain types; an external system's strings ("ACTIVE", "END_OF_LIFE") in a
  service or a contract mean the adapter leaks.

## Money

Money, durations and data volumes are integers: money in minor units as `long` (`bigint`), durations in
milliseconds, volumes in bytes. `decimal`, `float` and `double` do not appear in money logic: a rounding
rule written once beats a rounding error found in a report.

## Concurrent writers

An entity changed by both the API and background jobs carries a concurrency token: `xmin` on PostgreSQL
(`builder.Property<uint>("xmin").IsRowVersion()`), `rowversion` on SQL Server. The losing write fails
with `ConcurrencyException` instead of overwriting the other, the API answers 409, and the client reloads
and retries.

## No dead code

No defensive checks for what the infrastructure already guarantees (a row a foreign key guarantees, a
value a validator already refused). Validate at the boundaries: user input, external answers.

## Review flags

| Pattern | Issue |
|---------|-------|
| `SaveChangesAsync` without `BeginTransactionAsync` | Wrap the change in a transaction |
| `SaveChangesAsync` before `CommitTransactionAsync` | Redundant: remove |
| A change before `BeginTransactionAsync` | Move it inside the `try` |
| A `catch` in a transaction without a log line | Log the entity type and id before `throw` |
| A create a client may retry, without `IIdempotentRequest` | Run it through `IIdempotentExecutor` |
| `decimal` / `float` / `double` in money logic | `long` in minor units |
| `BackgroundJob.Enqueue` or `bus.PublishAsync` inline | A domain event handler |
| Sequential awaits on independent calls | `Task.WhenAll` |
| A constructor with 6+ dependencies | Likely more than one job |
| `IConfiguration["Key"]` in a service | Typed options |
| Raw filter values passed to a repository | A specification, `/add-specification` |

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
