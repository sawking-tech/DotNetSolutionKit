# ADR-001: Domain events run in three phases tied to the transaction

**Status:** Accepted, 2026-10-03

**Author:** Vladimir Savkin, the creator of DotNetSolutionKit

## Context

An aggregate raises a domain event when something happened to it: an order was placed, a balance ran
out. Different reactions to the same event need different moments:

- **Before the write is committed:** validate, enrich related data, put a message into the bus outbox.
  These have to succeed or fail together with the write.
- **After the commit:** send an e-mail, enqueue a job, write something else. These must not happen if the
  write failed, and cannot undo it if they fail themselves.
- **After a rollback:** compensate, clear a cache, log what was lost.

The common way in .NET is MediatR notifications published from the use case. The use case then decides
when to publish. Published before `SaveChanges`, a reaction happens even when the save fails. Published
after it, the reaction is outside the transaction, and a message put into the outbox at that point is
dropped. A third variant, dispatching from an override of `DbContext.SaveChanges`, gives one moment
instead of three, and only for code that goes through that override.

## Decision

Domain events are dispatched by EF Core interceptors, in three phases, each with its own handler
interface:

| Phase | Interface | Runs | Scope |
|---|---|---|---|
| Pre-save | `IDomainPreSaveHandler<TEvent>` | inside `SaveChanges`, before the transaction commits | the caller's: same `DbContext`, same transaction |
| Post-commit | `IDomainPostCommitHandler<TEvent>` | after a successful commit | a fresh DI scope per event |
| Rollback | `IDomainRollbackHandler<TEvent>` | after a rollback, with the exception | a fresh DI scope per event |

Events are harvested from entities that implement `IHasDomainEvents` (`AggregateRoot`, `EventfulEntity`),
so plain entities carry no event overhead. Because the interceptors sit on the `DbContext`, the pipeline
runs whatever saves: a repository, `IUnitOfWork`, or the context directly.

The pipeline also covers what the simple versions get wrong:

- **Cascades.** A pre-save handler may raise new events; pre-save repeats until no new events appear, up
  to 20 rounds, so a cascade is dispatched in the same save instead of being lost.
- **Nested commits.** A post-commit or rollback handler that commits its own transaction does not
  dispatch the outer events again: the dispatch is guarded and works on a snapshot.
- **Fresh scope after commit.** At that moment the caller's unit of work still holds the finishing
  transaction and its outbox window is closed; the fresh scope gives the handler its own context,
  transaction and outbox.
- **Work outside a request.** Message consumers and Hangfire jobs publish their scope to the interceptors
  (`DomainEventScopeFilter`, `DomainEventJobActivator`); without it, events raised there would be dropped.
- **Loud loss.** Events pending on an entity with no scope to dispatch them are logged as a warning
  instead of disappearing.

Details of each phase and its rules are in
[Events/Readme.md](../../template/src/common/NamespaceRoot.ProductName.Common.Application/Events/Readme.md).

## Alternatives considered

**MediatR notifications.** Rejected: the moment of dispatch is left to each use case, and neither moment
it can choose gives both "with the write" and "after the write".

**Dispatch in a `SaveChanges` override.** Rejected: one phase instead of three, and only for code that
calls that override.

**Every reaction through the message bus.** Rejected as the default: an in-service reaction would need a
broker and a consumer, and would run later rather than in the same call. The bus stays for reactions in
other services, published from pre-save into the outbox.

## Risks and how they are handled

| Risk | What happens | Handled by |
|---|---|---|
| The pipeline is our own implementation | It is maintained by us; a newcomer has to learn the phases | Tests of the dispatcher, the cascade, reentrancy and the job activator in `Common.Tests`; the Readme describes each phase's rules. It may later move into a library of its own |
| A post-commit handler is slow | The request that saved waits for it: post-commit runs synchronously | Rule: anything heavy goes to a job enqueued from the handler |
| A post-commit handler fails | The write stays committed; the reaction is lost | The failure is logged as an error and the remaining handlers still run; a reaction that must not be lost goes through the outbox in pre-save |
| Events raised while saving inside a post-commit handler | They are not dispatched: harvesting is suppressed during the outer dispatch | Documented limit; a follow-up that fans out its own events is enqueued as a job |
| Code that saves outside a request and outside consumers and jobs | Its events are dropped | A warning is logged; such code wraps its work in `DomainEventScopeContext.Use(scope)` |
| One class implements two phase interfaces for the same event | Its `Handle` runs in each phase | Documented limit: one class per phase |

## Consequences

- A message to another service leaves through the outbox only if the write commits.
- An e-mail or a job never goes out for a write that rolled back.
- Compensation sits in one place: a rollback handler gets the exception and the events of the failed
  write.
- A use case raises events on the aggregate and saves. It does not decide when reactions run; the
  handler's interface states that.
- Requests, consumers and jobs dispatch through the same pipeline, and so do tests on the in-memory
  database: `InMemoryTestExecutionContext` publishes the scope in `ActAsync`, see
  [ADR-005](005-testing-a-service.md).
