# Persistence

EF Core on PostgreSQL, or on SQL Server with `--Database mssql`. Each service has its own `DbContext` and
its own database schema, named after the service (`orders`, or `sales_orders` for `Sales.Orders`);
Hangfire gets a second schema next to it (`orders_hangfire`).

The sections below describe PostgreSQL; [SQL Server](#sql-server) lists what differs there. The code a
service writes against `Common` is the same for both.

## A service owns its schema

Services may share one PostgreSQL database, and nothing in the database stops two services from writing
into one schema. `PostgresSchemaGuard` does, at startup:

1. it creates the schema if it does not exist;
2. it reads the owner recorded in the schema's `_service_metadata` table, under a row lock;
3. it records the service as the owner if there is none, and refuses to start the service if the owner is
   another service.

The owner is the namespace of the service's domain. A copied connection string or a renamed service fails
at startup naming the owner, instead of writing into another service's tables.

When the database is still starting, as when the service and the database are started together, the
guard waits: up to 15 attempts, 2 seconds apart, while PostgreSQL answers "starting up" (SQLSTATE 57P03)
or its port refuses connections.

Why a guard of our own: PostgreSQL has no notion of a schema belonging to an application, and the
alternatives, a database or a database user per service, are infrastructure the template cannot assume.

## Migrations run under a lock

`MigrationRunner.RunMigrations` applies the migrations at startup, holding a PostgreSQL advisory lock
derived from the schema name. Two replicas of one service starting together do not migrate the same
schema twice; the second waits for the first and finds nothing to do.

A service with no migrations at all logs a fatal line naming the command that adds the first one; see
[generating a solution](../getting-started/generating-a-solution.md#add-the-first-migration).

## Repositories

A repository takes a query as a specification, and filtering, paging, sorting and includes are written
once, in `EntityFrameworkRepository`:

```csharp
public interface IOrderRepository : ISpecificationRepository<Order, Guid>;

public sealed class OrderRepository(OrdersDbContext context)
    : EntityFrameworkRepository<Order, Guid, OrdersDbContext>(context), IOrderRepository
{
    protected override IReadOnlyDictionary<string, string> SortFields { get; } =
        new Dictionary<string, string> { ["placedAt"] = nameof(Order.PlacedAt) };
}

var page = await orders.ListPageAsync(
    new QuerySpecification<Order>(new OrdersOfCustomer(customerId)).Include(o => o.Lines),
    request);
```

Why: [ADR-002](../adr/002-repositories-on-specifications.md).

## Case-insensitive search

`ICaseInsensitiveSearch` builds a search condition as an ordinary specification, so it combines with the
others through `&` and `|`:

```csharp
var query = new QuerySpecification<Order>(
    new OrdersOfCustomer(customerId) & search.GetSpecification<Order>(o => o.Customer.Email, term));
```

The PostgreSQL implementation translates it to `ILIKE` and escapes `%`, `_` and the escape character, so
user input matches literally. Tests on the in-memory database register an in-memory implementation
instead; see [ADR-005](../adr/005-testing-a-service.md).

## Readable numbers

An order or invoice number people read and type comes from a PostgreSQL sequence, taken before the
entity is created, so the events it raises in its constructor already carry the number:

```csharp
var number = await shortIds.GetNextAsync("orders.order_number_seq", ct);
var order = new Order(context, number, ...);
```

`IShortIdGenerator` is registered in a generated service; the sequence comes from a migration. Each value
is handed out once, even when its transaction rolls back, so gaps are normal. The name goes into the query
as a parameter, never into its text.

## Unit of work

`IUnitOfWork` is the transaction boundary of a use case; the service's `DbContext` implements it through
`DbContextBase`. A unique-constraint violation on save becomes a `UniqueViolationException`, and a write that
lost a race on a concurrency token (`xmin` on PostgreSQL, `rowversion` on SQL Server) a
`ConcurrencyException`; the API answers both with 409.

## Commands a client may repeat

A client retries when a connection drops or a queue redelivers, and it cannot tell whether the first
attempt went through. A command that must not create a second thing carries a key the client chose, and
`IIdempotentExecutor` runs its work once per key:

```csharp
public sealed record CreateOrder(..., string IdempotencyKey) : IIdempotentRequest;

return await idempotent.ExecuteAsync(request, "orders.create", _ =>
{
    var order = new Order(context, ...);
    orders.Add(order);
    return Task.FromResult(order.ToResponse());
}, ct);
```

- The first request does the work and records its key and answer in the same transaction; a repeat gets
  that answer and does nothing.
- Two requests with one key at the same moment both do the work, and the unique index on the log lets one
  commit. The other rolls back, drops what it tracked, and answers with the winner's result.
- A key used for another operation is refused with 409 `IDEMPOTENCY_KEY_REUSED`. A key shorter than 16 or
  longer than 128 characters is refused before anything runs.
- A failed attempt records nothing, so the retry does the work. A duplicate the work itself refuses, such
  as a taken name, reaches the caller as that conflict: a retry would fail the same way.
- `ExecuteOnceAsync` is for an answer that must not be stored, such as a secret shown once; a repeat is
  refused with 409 `IDEMPOTENCY_ALREADY_CARRIED_OUT`.
- Keys are scoped to the tenant the caller acts for, or to the account without one.

The log is a table in the service's schema. It is not there by default; a service that needs it adds it
to its model and registers the executor:

```csharp
// OnModelCreating, then add a migration
modelBuilder.AddIdempotencyLog();

// Infrastructure DependencyInjection
services.AddIdempotency<OrdersDbContext>();
```

## SQL Server

`--Database mssql` generates the solution on SQL Server. Each piece above has a SQL Server implementation
behind the same seam, and only one of the two goes into a generated solution:

| Piece | PostgreSQL | SQL Server |
|---|---|---|
| EF provider | Npgsql | `Microsoft.EntityFrameworkCore.SqlServer` |
| schema guard | `PostgresSchemaGuard`, row lock | `SqlServerSchemaGuard`, `sp_getapplock` held by the transaction |
| migration lock | advisory lock | `sp_getapplock` held by the session |
| unique violation | SQLSTATE 23505 | errors 2627 and 2601 |
| case-insensitive search | `ILIKE` | `LIKE` under the `Latin1_General_100_CI_AS` collation |
| readable numbers | `nextval` | `sp_sequence_get_range` |
| Hangfire storage | `Hangfire.PostgreSql` | `Hangfire.SqlServer` |
| outbox | MassTransit on PostgreSQL | MassTransit on SQL Server |

Which of the two a solution has is known in one place, `DatabaseProvider` in `Common.Infrastructure`: the
service and `Common` ask it for the EF provider, the schema guard, the migration lock, the outbox's lock
statements and statistics, the search and the readable numbers, and never name a provider themselves.
Hangfire keeps a storage package per database, so a service's `DependencyInjection.BackgroundJobs.cs` is the
second place that knows it. A third database touches these two files and the implementations behind them.

What a team notices:

- The guard creates the database of the connection string when there is none: the SQL Server container
  starts with `master` only. An existing database is used as it is: the right to create a database is
  needed only when there is none yet.
- The guard waits up to 30 attempts, 2 seconds apart, while the server is starting (errors 4060, 53, 40
  and timeouts).
- The search compares under a case-insensitive collation whatever the column's own collation, and escapes
  `%`, `_` and `[` with `/`. Tests on the in-memory database register `InMemorySqlServerCaseInsensitiveSearch`,
  which matches the same way.
- Hangfire's tables are installed at startup under one lock for the whole database: two services
  installing into one database at the same moment deadlock in SQL Server, and Hangfire gives up after
  three attempts.
- A sequence for readable numbers is `CREATE SEQUENCE` in a migration; the generator takes its name as a
  parameter, as on PostgreSQL.
- The migrations of a service generated for PostgreSQL do not apply to SQL Server: a service is generated
  for one database from the start, and its migrations are added against it.

## Tests

`Common.Tests` covers the base repository, specifications, sorting, `DbContextBase` and the lock key of
the migration runner. The guard, the migrations and the `ILIKE` translation need PostgreSQL and belong to
integration tests. The idempotent executor is tested on the solution's database: a retry, a race on one
key, a reused and an invalid key, a failed attempt, the work's own duplicate, two tenants with one key.
On SQL Server the same integration tests need `TEST_SQLSERVER`; see [testing](testing.md).
