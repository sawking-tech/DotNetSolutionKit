---
name: add-tests
description: Scaffold tests for a service method or a whole service of this solution - sociable tests on the in-memory database, Shouldly, real repositories, PostgreSQL only where the in-memory provider cannot show it.
---

Generate tests for a service method using the solution's test infrastructure (`Common.Testing`).

## Usage

**Single method:** `/add-tests <ServiceName>.<MethodName>` - e.g. `/add-tests OrderService.PlaceAsync`

**Full service:** `/add-tests <ServiceName>` - a test base and one fixture per public method.

The approach is [ADR-005](https://dotskit.sawking.tech/docs.html#adr-005); how to start:
[testing](https://dotskit.sawking.tech/docs.html#testing).

## What to do

1. Read the class under test.
2. Read its test base if one exists; if not, scaffold one (below).
3. One fixture per method under test, in a file named after both: `OrderService.Place.Tests.cs`.

## Testability checklist - before writing tests

Flag these to the user first:

| Code smell | Problem |
|-----------|---------|
| `new SomeDependency()` inside the class | Not injectable: a test cannot replace it |
| `DateTime.UtcNow` in an entity or service | Use `IDomainExecutionContext.TimeProvider`; tests set the time |
| `BackgroundJob.Enqueue` or `bus.PublishAsync` inline in a service | Move to a domain event handler |
| `if` / `switch` in a controller | No branching in controllers |
| A policy not injected as an interface | Cannot be replaced in a service test |
| A constructor with 6+ dependencies | Likely an SRP violation, and hard to set up |

## Which kind of test

Each kind of code gets the cheapest test that can fail when the code is wrong:

| Subject | Built with | Replaced | Folder |
|---|---|---|---|
| Service method | `InMemoryTestExecutionContext<TService, TDbContext>` | only what leaves the service: mail, tokens, other services, time | `Services` |
| Consumer | the same context, the consumer as the class under test, a built `ConsumeContext` | as for a service | `Consumers` |
| Domain event handler | the handler with its ports mocked | the ports it calls | `EventHandlers` |
| Job | the job with its ports mocked; verify the calls | the ports it orchestrates | `Jobs` |
| Entity rule, policy, validator | `new`, no container | nothing | `Policies`, `Validation` |
| What needs the real database | `PostgresDbTestExecutionContext<TService>` | nothing | `Integration` |

A scenario gets one test of the cheapest kind that covers it, never a unit test and an integration test
for the same thing.

## Test base

```csharp
public abstract class OrderServiceTestBase
{
    protected InMemoryTestExecutionContext<OrderService, OrdersDbContext> CreateTestContext()
    {
        var context = new InMemoryTestExecutionContext<OrderService, OrdersDbContext>();

        var mail = new Mock<IMailSender>();
        context.Register(mail);                    // the mock, to verify after the act
        context.Register(mail.Object);             // what the class under test receives

        context.Register<IOrderRepository, OrderRepository>();
        context.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrdersDbContext>());
        context.Register<IDomainExecutionContext, TestDomainExecutionContext>(
            new TestDomainExecutionContext(new UserContextMock(), new TimeProviderMock(Now)));
        return context;
    }
}
```

Real repositories and the real `DbContext`; mocks only at the boundary of the service. A mock registered
with `Register` is the same instance in every scope of the test, so it is set up before the act and
verified after it.

## Fixture

```csharp
[TestOf(typeof(OrderService))]
[RunsInParallel]
public class OrderServicePlaceTests : OrderServiceTestBase
{
    [Test(Description = "A placed order is saved with its lines")]
    public async Task Should_SaveTheOrder_When_TheCartHasLines()
    {
        await using var ctx = CreateTestContext();
        await ctx.ArrangeAsync(Customer);

        var orderId = await ctx.ActAsync(s => s.PlaceAsync(new PlaceOrder(Customer.Id, Lines), CancellationToken.None));

        await ctx.AssertAsync(async db =>
            (await db.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == orderId)).Lines.Count.ShouldBe(2));
    }
}
```

`ArrangeAsync`, `ActAsync` and `AssertAsync` run in separate scopes, so the assertion reads what was saved,
not an entity still tracked by the context that changed it. Every test has its own in-memory database,
so fixtures run in parallel.

The test is the same under NUnit and xUnit: the attributes come from `TestFramework.cs` in the test
project, the one file that knows the framework. Never put a framework's own attribute (`[Fact]`,
`[TestFixture]`, `[Parallelizable]`, `[Trait]`, `[Category]`) or a `#if` on the framework into a test. Test
classes are `public`, since xUnit runs only public ones.

## Domain event handlers in a service test - selective, never an assembly scan

`ActAsync` runs the domain event interceptors, so the handlers registered in the test run as they do in
the service. Register only the handlers the test needs:

```csharp
context.Services.AddDomainEvents(typeof(PublishOrderPlaced));   // this handler only
context.Services.AddDomainEvents();                             // the dispatcher only, no handlers
```

An assembly scan would pull every handler in, and one handler's dependency becomes a failure of an
unrelated test. `AddDomainEvents` refuses, as the service does, a class that implements the pre-save and
the post-commit phase for one event.

## Search, the bus, the user

- A service that searches with `ICaseInsensitiveSearch` gets `InMemoryCaseInsensitiveSearch` from
  `Common.Tests.Stubs` in a service test. Never a hand-made mock or `string.Contains`: the stub takes its
  pattern from the database implementation itself, so it escapes `%`, `_` and the escape character and
  matches as the database does; a `Contains` mock passes patterns the database answers differently.
- An integration test registers the production implementation, never the stub: the point of the test is
  the SQL.
- Messages sent through `IMessageBus` are checked with `RecordingMessageBus` from `Common.Tests.Stubs`.
- The actor and the time come from `TestDomainExecutionContext`, `UserContextMock` and `TimeProviderMock`.

## Integration tests - only what the in-memory provider cannot show

Against the real database, and only for: transactions, rollback and the outbox; foreign keys, unique
indexes and other constraints; case-insensitive search and raw SQL; migrations and the schema guard;
concurrency tokens.

```csharp
[Test, Integration]
public async Task Should_RejectADuplicateNumber()
{
    await using var ctx = await PostgresDbTestExecutionContext<OrderService>.CreateAsync();
    ...
}
```

The connection string comes from `TEST_POSTGRES` (`TEST_SQLSERVER` with `--Database mssql`); without it the
test is skipped with that reason, so a plain `dotnet test` needs no database. `eval "$(bash tests/servers/up.sh)"`
starts every test server of the solution and sets the variables. Each test gets a database of
its own, cloned from one migrated once per run.

Anything the in-memory provider cannot run (`ExecuteSqlRawAsync`, `SqlQueryRaw`, a constraint, a
trigger) needs both: the service test with that call behind a port, and an integration test of the port.
A mock without the integration test is false confidence.

## Rules

- **Async.** Every test is `async Task`; `await` every awaitable, never `.Result` or `.Wait()`.
- **Cancellation.** Pass `CancellationToken.None` explicitly where the method takes a token, so it is
  visible that cancellation is wired. A test of cancellation itself cancels a `CancellationTokenSource`
  inside the act and asserts `await Should.ThrowAsync<OperationCanceledException>(...)`.
- **Disposal.** `await using var ctx = CreateTestContext();` - the contexts are `IAsyncDisposable`.
- **Shouldly only:** `.ShouldBe()`, `.ShouldNotBeNull()`, `await Should.ThrowAsync<T>(() => ...)`. No
  `Assert.*`.
- `[Test(Description = "...")]` on every test; names `Should_<Outcome>_When_<Condition>`; `[TestOf]` on
  every fixture.
- One file per method under test.
- Mock only what leaves the service; never a repository.
- Mock matching by value (`c => c.Id == id`), never by reference.

## Tests land with the code

A method and its tests are in the same commit: `git checkout <sha>` builds and passes its own tests. "Code
now, tests next PR" is not allowed. If the tests of one commit grew too large to review, the change was too
big: split the change, not the change from its tests.

## No direct tests of entity methods

Entity methods (state transitions, invariant checks, marker setters) are tested through the service tests
that exercise them. A direct entity test is warranted only for a domain rule with branching that no
service exercises in full, or to lock in a bug with a focused reproduction next to its fix. Prefer adding a
service scenario that reaches the rule.

When a service test also covers a handler, a consumer or a job, say so in a comment, so nobody writes a
second test for it:

```csharp
[Test(Description = "A placed order is announced on the bus")]
// Covers PublishOrderPlaced (pre-save) as well: the test asserts the published message.
public async Task Should_PublishOrderPlaced_When_TheOrderIsSaved() { ... }
```

Partial coverage (only the happy path of the handler) still needs explicit handler tests for its failure
paths.

## Scenarios to cover, at least

- the happy path;
- not found: `NotFoundException`;
- a business rule broken: `BusinessLogicException`;
- a duplicate or a conflict: `ConflictException`;
- an actor without access: `AccessDeniedException`.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
