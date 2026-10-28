---
name: add-entity
description: DDD rules for a domain entity or aggregate of this solution - rich model, policy layer, domain events, three-tier defence, EF configuration that follows its neighbours.
---

How to create a domain entity or aggregate.

## Usage
`/add-entity <EntityName>` - e.g. `/add-entity Invoice`

---

## Aggregate root or entity

| Base (`Common/Domain`) | When |
|-----|------|
| `AggregateRoot<TId>` | owns child entities, raises domain events, enforces business invariants |
| `Entity<TId>` | a lookup, a join table, a child of another aggregate - no events, no invariants of its own |

An entity that raises events is an `AggregateRoot<TId>`: only `EventfulEntity<TId>`, its base, has
`AddDomainEvent`. If an entity cannot be an aggregate root by design, it implements `IHasDomainEvents`
itself. A repository with `Add` / `Remove` for a plain `Entity<TId>` breaks the aggregate boundary; the
shared `ISpecificationRepository` accepts aggregate roots only.

## One responsibility per layer

| Layer | Responsibility |
|-------|---------------|
| Domain entity | Enforce invariants, raise domain events |
| Policy | Access: who may do what, from the actor and the data |
| Repository | Data access only - no business logic |
| Domain event handler | One side effect per handler (one phase, one concern) |

An entity method that does more than enforce an invariant or raise an event is two methods.

## Rich domain model - invariants only

The model guards its own invariants in its constructor and methods. It does not check access: that is
the policy's.

```csharp
// Domain: guard the invariant, not the actor
public void EnsureCanAddLine()
{
    if (Status != OrderStatus.Draft)
        throw new BusinessLogicException("Lines are added to a draft order only");
}

// Domain: change the state, then raise the event
public void Place(IDomainExecutionContext context)
{
    if (Status == OrderStatus.Placed) return; // idempotent
    Status = OrderStatus.Placed;
    MarkUpdated(context);
    AddDomainEvent(new OrderPlaced(context, Id, Number));
}
```

**Never in the domain:**
- `IUnitOfWork`, a repository, `IServiceProvider` - no infrastructure in the domain;
- access checks (who the actor is, which permission they have) - that is the policy;
- `DateTime.UtcNow` - use `context.TimeProvider.GetUtcNow()`; `MarkCreated(context)` and
  `MarkUpdated(context)` set `CreatedAt` and `UpdatedAt` from it.

## Extend without modifying

New behaviour comes as new types, not as changes to existing ones:
- a new filter criterion is a new **specification**, composed with `&` / `|`;
- a new side effect is a new **domain event handler**;
- a new variant of an external integration is a new implementation of its port.

Never add `if (type == X)` branches to an entity; add a handler or a strategy instead.

## Policy layer - access control

A policy lives in the application layer, one interface per aggregate area:

```csharp
public interface IOrderPolicy
{
    void EnsureCanView(Order order, IUserContext actor);
    void EnsureCanCancel(Order order, IUserContext actor);
}
```

- A policy throws `AccessDeniedException`; the shared handler answers 403.
- A platform user and a tenant's user have different rules: check both paths.
- A service method without its policy call is a bug.
- Where tenants form a tree, the policy compares paths with `HierarchyRules` (`--HierarchyRules`), so the
  tree means the same in every service: [hierarchy rules](https://dotskit.sawking.tech/docs.html#hierarchy-rules).

## Three-tier defence

Every operation has all three:

```
Controller    [RequiredPermissions(OrderPermissions.Cancel)]   <- permission
Service       policy.EnsureCanCancel(order, actor)            <- access to this data
Domain        order.EnsureCanCancel()                         <- invariant
```

A missing layer is a finding: [authentication and permissions](https://dotskit.sawking.tech/docs.html#auth).

## Aggregate boundaries

Child entities are changed through their aggregate root only.

```csharp
// Wrong: a child changed through a repository of its own
lineRepository.Add(new OrderLine(...));

// Right: through the root
order.AddLine(productId, quantity, price, context);
await unitOfWork.CommitTransactionAsync(ct);
```

## Identity - generated in the constructor

**Every Guid entity sets `Id = Guid.NewGuid()` in the first line of its constructor.**

```csharp
public Order(IDomainExecutionContext context, string number, Guid customerId)
{
    Id = Guid.NewGuid();          // first, before any domain event
    Number = number;
    CustomerId = customerId;
    Status = OrderStatus.Draft;
    MarkCreated(context);
    AddDomainEvent(new OrderCreated(context, Id, Number));   // carries the real Id
}
```

**Why not a database default alone:** the pre-save handlers run inside `SaveChanges`, before the INSERT
reaches the database. An event created in the constructor with `Id` still `Guid.Empty` carries
`Guid.Empty` into the outbox, and every consumer receives a broken identifier.

The EF configuration keeps a database default anyway, for rows inserted by raw SQL (data fixes, scripts)
that bypass EF; the application always sets the value, so EF never uses it:

```csharp
builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");   // PostgreSQL
builder.Property(x => x.Id).HasDefaultValueSql("NEWID()");             // SQL Server
```

**Integer identifiers** have the same problem when the entity's events carry `Id`. In order of
preference:
1. take the number from a sequence before constructing the entity: `IShortIdGenerator.GetNextAsync`, see
   readable numbers in [persistence](https://dotskit.sawking.tech/docs.html#persistence);
2. `UseHiLo()`, with the events raised after `Add`, not in the constructor;
3. an `IDomainPostCommitHandler`, if that event does not need the outbox guarantee.

## Domain events - when to raise

- After the state change, never before.
- The `IDomainExecutionContext` comes from the calling service; the entity never resolves it.
- `OccurredAt` comes from `context.TimeProvider.GetUtcNow()`.
- Side effects (a bus message, a job) live in handlers, not in the entity or the service.

The event and its handlers: `/add-domain-event`.

## Before writing the EF configuration - open the neighbours

Open two or three configurations in the same folder (`EntityFramework/Configurations/`, or wherever the
service keeps them) and copy their conventions: column names, enum storage, index names, delete
behaviour, value generation. Do not introduce a new convention where the code already has one. Deviate
only when the entity needs a different shape, and say why in a comment.

The service's `DbContext` applies every configuration in its assembly
(`ApplyConfigurationsFromAssembly`); a new `IEntityTypeConfiguration<T>` needs no registration, only a
`DbSet` and a migration (`/ef-migration`).

## Enums - one definition, stored as text

**Never duplicate an enum between the service's domain and `Common.Contracts`.** An enum that appears in
an API contract or a bus message is defined once, in `Common.Contracts`, and the entity uses it directly.
An enum that never leaves the service stays in its domain.

A copy needs a mapping layer and drifts out of sync.

```csharp
builder.Property(o => o.Status)
    .HasConversion<string>()
    .HasMaxLength(64)
    .IsRequired();
```

**Why text, not an integer:**
- a value added in the middle of the enum does not change what existing rows mean;
- reordering members does not corrupt data silently;
- rows read without a codebook.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
