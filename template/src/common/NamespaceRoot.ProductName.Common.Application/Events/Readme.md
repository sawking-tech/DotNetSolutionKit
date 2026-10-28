# Domain Events System (3-Phase Pipeline)

A domain event orchestration engine integrated with the **Entity Framework Core** transaction
lifecycle via **IUnitOfWork**. It provides a clean separation between synchronous domain logic,
reliable messaging, and post-commit side effects.

### Core Architecture Principles

1. Encapsulation by Design: Technical cleanup methods (ClearDomainEvents) are hidden from the Application Layer using Explicit Interface Implementation (IHasDomainEvents).
2. Resource Efficiency (Opt-in Events): Domain event support is decoupled from the base Entity<TId>. Events are only processed for entities implementing IHasDomainEvents (like AggregateRoot<TId>). This keeps lightweight entities (lookups, join tables) free from event-related memory overhead.
3. DI Resilience: Infrastructure resolution is safe for DbContextPool and background tasks (Data Seeding/Hangfire), avoiding "Scoped service from root provider" errors.
4. Phase Isolation: PostCommit / Rollback handlers execute in their own DI scope, so they can safely open transactions and publish to the bus regardless of the state of the scope that triggered them.

### Core Handlers Interfaces

Depending on the business requirements, implement one or more of these specialized interfaces:

- **IDomainPreSaveHandler<TEvent>**: Executed within the transaction before saving. Used for validations, data enrichment and Outbox publishing.
- **IDomainPostCommitHandler<TEvent>**: Executed after a successful commit, in a fresh DI scope. Used for follow-up writes, bus publishing, Hangfire jobs, analytics.
- **IDomainRollbackHandler<TEvent>**: Executed if the transaction fails, in a fresh DI scope. Used for compensation logic or detailed error logging.

---

### 3-Phase Architecture - execution model

The pipeline dispatches events across three distinct execution windows. **The scope a handler
runs in is part of the contract** - it defines what the handler is allowed to do:

1. **Phase 1: Pre-Save (Transactional Consistency)**
   Executed inside `SaveChangesAsync`, before the transaction is finalized, **in the ambient
   scope** - same DbContext, same open transaction as the triggering write.
- **Usage:** Validations, data enrichment, and **MassTransit bus-outbox** publishing.
- **Guarantee:** If the DB transaction fails, no messages are sent to the broker. Everything is atomic.
- **Rules:** Do NOT call `BeginTransactionAsync` / `CommitTransactionAsync` - you are already
  inside the caller's transaction. Throwing rolls the whole write back (that is the point).

2. **Phase 2: Post-Commit (Success Side-Effects)**
   Executed once the write is final, synchronously and **in a FRESH DI scope per event**: inside the
   ambient `CommitAsync` call when a transaction commits (EF transaction interceptor); when a save with
   no transaction open around it returns, since EF Core sends a single statement without a transaction
   and no transaction event comes (`SavedChanges`); and on a provider without relational transactions,
   such as the in-memory one of the service tests, when `CommitTransactionAsync` returns.
- **Usage:** Follow-up DB writes, bus publishing, **Hangfire** enqueuing, analytics, notifications.
- **Why the fresh scope:** the ambient UnitOfWork still holds the completing transaction at this
  moment (`BeginTransactionAsync` on it would throw «A transaction is already in progress») and
  its bus-outbox window is already closed (a publish there is silently dropped). The fresh scope
  gives the handler its own DbContext, transaction capability and outbox window.
- **Rules:** For DB writes or bus publishes, open your OWN transaction:
  `Begin -> mutate / publish -> SaveChanges -> Commit`. A publish outside a transaction+SaveChanges
  is dropped by the bus outbox. Exceptions are logged (Error) and swallowed - they cannot undo
  the already-committed write, and the remaining handlers still run.
- **Limitation:** domain events raised on entities saved inside a PostCommit handler are NOT
  dispatched (the outer dispatch is in progress; harvesting is suppressed to prevent cascades).
  If the follow-up write must fan out its own events, enqueue a job instead.
- **Latency:** handlers run synchronously inside the triggering call - keep them short; anything
  heavy goes to Hangfire.

3. **Phase 3: Rollback (Fault Tolerance)**
   Executed if the transaction rolls back, **in a FRESH DI scope per event** (same reasons as
   Phase 2 - the ambient transaction is mid-rollback).
- **Usage:** Logging specific errors, clearing external cache, or compensatory actions.
- **Context:** Handlers receive the `Exception` that caused the failure via `HandleRollback`.

---

### Implementation Example

Each handler class must implement **exactly one phase interface**. One class = one phase.

> The pre-save and the post-commit interfaces share one `Handle` method, so a class implementing both
> for one event would run the same code in both phases. Registration refuses such a class at startup and
> names it (`DomainEventHandlerPhases`); make it two classes. A rollback handler has `HandleRollback` of
> its own and goes with either.

```C#
// Phase 1 - publish to the bus outbox inside the SAME transaction as the write
public class PasswordResetOutboxHandler : IDomainPreSaveHandler<PasswordResetRequestedEvent>
{
    public async Task Handle(PasswordResetRequestedEvent @event, CancellationToken ct, object? data = null)
    {
        await _publishEndpoint.Publish(@event, ct);
    }
}

// Phase 2 - follow-up write + publish in the handler's OWN transaction (fresh scope)
public class StockLevelCheckHandler : IDomainPostCommitHandler<OrderPlacedEvent>
{
    public async Task Handle(OrderPlacedEvent @event, CancellationToken ct, object? data = null)
    {
        // the check internally does: Begin -> flip a low-stock marker + bus.PublishAsync -> SaveChanges -> Commit
        await _stockLevels.CheckAfterOrderAsync(@event.OrderId, ct);
    }
}

// Phase 3 - compensation on failure
public class PasswordResetRollbackHandler : IDomainRollbackHandler<PasswordResetRequestedEvent>
{
    public Task HandleRollback(PasswordResetRequestedEvent @event, Exception? exception, CancellationToken ct)
    {
        // Log or compensate
        return Task.CompletedTask;
    }
}
```
---

### Infrastructure Guarantees

1. **UOW Independence**: The system is built on EF Core Interceptors. It works seamlessly whether you use `IUnitOfWork`, `Repository Pattern`, or direct `DbContext` calls. Any operation triggering `SaveChangesAsync` or DB transaction events will fire the pipeline.
2. **Recursion & Cycle Protection**: The `IsDispatching` flag guards BOTH interceptors. PreSave skips re-harvesting when a handler triggers a nested `SaveChangesAsync`; the transaction interceptor skips re-dispatching (and does not clear the storage mid-iteration) when a PostCommit / Rollback handler commits or rolls back its own transaction in a context that resolves to the same scoped storage.
3. **Immediate Event Clearing**: Domain events are cleared from entities strictly before dispatching starts, ensuring no duplicates during nested saves.
4. **Transaction Resilience**: `IDomainEventStorage` is cleared within `finally` blocks, preventing "event leaking" between requests in the same DI Scope.
5. **Unit of Work Sync**: Calling `IUnitOfWork.DiscardChanges()` explicitly clears all pending domain events, keeping memory and database state perfectly synchronized.
6. **Loud Loss**: If entities carry pending events but no DI scope is resolvable (see Scope Resolution below), the PreSave interceptor logs a WARNING naming the context: events dropped without a trace are the failure this pipeline is built to prevent.

---

### Scope Resolution - where the pipeline finds its services

The interceptors are singletons and resolve the scoped
`IDomainEventStorage` / `IDomainEventDispatcher` per operation, in priority order:

1. **HTTP requests** - `IHttpContextAccessor.HttpContext.RequestServices` (automatic).
2. **MassTransit consumers** - ambient `DomainEventScopeContext`, set automatically by the
   globally registered `DomainEventScopeFilter`.
3. **Hangfire jobs** - ambient `DomainEventScopeContext`, set by `DomainEventJobActivator` for the
   job's own scope.
4. **Everything else** - no scope, so events are skipped (WARNING if events were pending).

**Self-managed processing scopes** (raw RabbitMQ consumers, custom background loops) are case 4
unless they publish their scope explicitly. Wrap the handling in:

```C#
using var scope = _scopeFactory.CreateScope();
using (DomainEventScopeContext.Use(scope.ServiceProvider))
{
    // ... resolve services and process the message ...
}
```

Without this wrapper the entities such a consumer changes raise events that no interceptor can see,
and their PostCommit handlers never run.

---

### Registration & Setup

#### Step 1: Register Infrastructure & Handlers
In your microservice **Program.cs**:
```C#
// Fast way (registers Core, Persistence, and scans assembly for Handlers)
services.AddDomainEvents(typeof(MyAssemblyMarker).Assembly);

// Or Granular way
services.AddDomainEventCore();
services.AddDomainEventPersistence();
services.AddDomainEventHandlers(typeof(MyAssembly).Assembly);
```
#### Step 2: Configure DbContext
Inject all interceptors into your **DbContext** using the fluent helper:

```C#
services.AddDbContext<MyDbContext>((sp, options) =>
{
options.UseNpgsql(connectionString);
options.ApplyDomainEventInterceptors(sp);
});
```

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
The current version of domain events: [dotskit.sawking.tech](https://dotskit.sawking.tech/docs.html#domain-events).
