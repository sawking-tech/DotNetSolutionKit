---
name: add-domain-event
description: Scaffold a domain event of this solution and its handlers in the three phases (pre-save, post-commit, rollback), idempotent where delivery repeats.
---

Implement a domain event: the event record and its handlers in the three-phase pipeline.

## Usage
`/add-domain-event <EventName>` - e.g. `/add-domain-event OrderCancelled`

The pipeline: [domain events](https://dotskit.sawking.tech/docs.html#domain-events), why three phases:
[ADR-001](https://dotskit.sawking.tech/docs.html#adr-001).

## What to do

Ask the user (in their language):
1. Which service?
2. Which aggregate raises the event?
3. Which phases are needed: pre-save, post-commit, rollback?

Then scaffold the files below.

---

## Idempotent handlers - mandatory where delivery repeats

Bus delivery is at-least-once, and Hangfire retries jobs. Every pre-save handler that publishes to the
bus, every post-commit handler that enqueues a job or calls an external API, and every consumer **must**
be idempotent on redelivery.

Pick the guard that fits the side effect:

- **Correlation log.** A table `<flow>_log (correlation_id PRIMARY KEY)` in the service's schema; insert on
  entry, and if the row already exists the cascade was processed - return without the side effect.
- **State marker.** If the entity carries an in-flight marker, guard the entry: `if (!order.IsReservationPending) return;`.
  A rerun after the marker was cleared does nothing.
- **Job key.** Enqueue a job under a key derived from the correlation id, so a second enqueue is the same
  job.
- **Stable message.** A message carries the same identifier and shape on every retry; a consumer
  deduplicates by it.

Name the guard in the handler's XML doc. The handler's tests cover redelivery: `Handle` twice with the
same input, exactly one side effect.

## Step 1 - Event record

Location: the service's domain project, next to the aggregate, e.g. `Domain/Orders/Events/<EventName>.cs`.

```csharp
/// <summary>
/// Raised when an order is cancelled.
/// </summary>
public sealed record OrderCancelled(
    IDomainExecutionContext Context,
    Guid OrderId,
    string Reason) : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = Context.TimeProvider.GetUtcNow();
}
```

**Rules:**
- a `record`;
- `IDomainExecutionContext Context` first;
- `OccurredAt` from `Context.TimeProvider.GetUtcNow()`, never `DateTime.UtcNow`;
- an XML `<summary>` saying when it is raised.

## Step 2 - Raise it from the aggregate

```csharp
public void Cancel(IDomainExecutionContext context, string reason)
{
    EnsureCanCancel();
    Status = OrderStatus.Cancelled;
    MarkUpdated(context);
    AddDomainEvent(new OrderCancelled(context, Id, reason));
}
```

- Only `AggregateRoot<TId>` (through `EventfulEntity<TId>`) has `AddDomainEvent`.
- Raise after the state change, never before.
- The context comes from the calling service; the entity never resolves it.

## Step 3 - Handlers

Location: the service's application project, e.g. `EventHandlers/<HandlerName>.cs`. They are found by
scanning that assembly; the service already calls `AddDomainEvents(typeof(ApplicationMarker).Assembly)`
and its `DbContext` already applies the interceptors, so a new handler needs no registration.

### Phase 1 - pre-save: the outbox, inside the transaction

```csharp
public sealed class PublishOrderCancelled(IMessageBus bus, ILogger<PublishOrderCancelled> logger)
    : IDomainPreSaveHandler<OrderCancelled>
{
    public async Task Handle(OrderCancelled e, CancellationToken ct, object? data = null)
    {
        logger.LogInformation("Publishing {Event}: {OrderId}", nameof(OrderCancelled), e.OrderId);
        await bus.PublishAsync(new OrderCancelledV1 { OrderId = e.OrderId, Reason = e.Reason }, ct);
    }
}
```

**When:** publishing through the outbox (`--Messaging outbox`): the message is stored with the change
that raised it and is sent after the commit.
**Errors:** a throw rolls the whole write back - that is the point.
**Never** call `BeginTransactionAsync` / `CommitTransactionAsync` here: the handler is inside the caller's
transaction.

### Phase 2 - post-commit: after the commit

```csharp
public sealed class ScheduleRefund(IBackgroundJobClient jobs) : IDomainPostCommitHandler<OrderCancelled>
{
    public Task Handle(OrderCancelled e, CancellationToken ct, object? data = null)
    {
        jobs.Enqueue<IRefunds>(r => r.RefundAsync(e.OrderId, CancellationToken.None));
        return Task.CompletedTask;
    }
}
```

**When:** Hangfire jobs (`-H`), notifications, follow-up writes.
**Scope:** a fresh DI scope per event. A follow-up write or bus publish opens its own transaction:
`Begin -> change / publish -> Commit`; a publish outside a transaction is dropped by the bus outbox.
**Errors:** logged and swallowed - they cannot undo the committed write, and the other handlers still run.
**Limit:** events raised by entities saved inside a post-commit handler are not dispatched. A follow-up
that must fan out its own events enqueues a job.

### Phase 3 - rollback: compensation

```csharp
public sealed class LogFailedCancellation(ILogger<LogFailedCancellation> logger) : IDomainRollbackHandler<OrderCancelled>
{
    public Task HandleRollback(OrderCancelled e, Exception? exception, CancellationToken ct)
    {
        logger.LogError(exception, "Cancellation of {OrderId} rolled back", e.OrderId);
        return Task.CompletedTask;
    }
}
```

The method is `HandleRollback(event, exception, ct)`, not `Handle`.

---

## One class, one phase

The pre-save and the post-commit interfaces share one `Handle` method, so a class implementing both for
one event would run the same code twice. Registration refuses such a class at startup and names it; make
it two classes. A rollback handler has its own `HandleRollback` and goes with either.

## The caller wraps the change in a transaction

A pre-save publisher writes the outbox row inside `SaveChanges`; the business write and the outbox row
must commit together:

```csharp
await unitOfWork.BeginTransactionAsync(ct);
try
{
    order.Cancel(context, reason);                 // raises the event
    await unitOfWork.CommitTransactionAsync(ct);   // SaveChanges + commit: the order and the outbox row together
}
catch
{
    await unitOfWork.RollbackTransactionAsync(ct);
    throw;
}
```

This covers cascades too: a pre-save handler that changes a second aggregate raises a second event, the
interceptor dispatches it in the same save, and its publisher writes in the same transaction. Without the
explicit transaction, a later change that adds a `SaveChangesAsync` between the steps splits the outbox
row into a separate implicit transaction.

## Anti-pattern - never call the dispatcher from service code

Domain events (in-process, dispatched to the phase handlers) and bus messages (`IBusEvent`,
`IBusCommand` through `IMessageBus`) are different things. Mixing them loses messages silently.

```csharp
// WRONG
await unitOfWork.SaveChangesAsync(ct);                          // the save already returned
await eventDispatcher.DispatchPostCommitAsync([new OrderCancelled(context, order.Id, reason)]);
```

The handler publishes with `bus.PublishAsync`; under the bus outbox the publish goes into a buffer tied
to the current `DbContext`, flushed by the next `SaveChanges`. There is no next save: the scope is
disposed with the message still in memory. No outbox row, nothing reaches the broker, no consumer runs,
and the handler's "Publishing" log line says it was sent.

```csharp
// RIGHT: raise it on the aggregate and save
order.Cancel(context, reason);
await unitOfWork.CommitTransactionAsync(ct);
```

The dispatcher is wired into the interceptors; application code never calls it. Injecting
`IDomainEventDispatcher` into a service is the smell: move the trigger onto the aggregate.

A direct `bus.PublishAsync` from service code inside an open transaction is a different thing: it is the
ordinary way to publish and it goes through the outbox.

## Guarantees of the pipeline

- **Re-entry guard:** a pre-save handler that calls `SaveChangesAsync` does not dispatch the same events
  again.
- **Events cleared before dispatch:** a nested save does not raise already-dispatched events again.
- **Storage cleared in `finally`:** no event leaks into the next request, even when a handler throws.
- **Loud loss:** entities with pending events and no scope to dispatch them in produce a warning naming the
  context.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
