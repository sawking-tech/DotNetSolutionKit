# Testing

Which kind of test a piece of code gets, and why most of them run on an in-memory database:
[ADR-005](../adr/005-testing-a-service.md). This page is how to start.

The service's test project references `Common.Testing`: the test contexts, the test databases of the solution's database,
stubs for time and the user, and the rule checks. It carries no test framework; the service's test
project picks one, and sets `TestSkip.Handler` to how that framework skips a test. `Common.Tests` is the
test project of `Common` itself.

`--TestFramework xunit` generates the service's tests on xUnit v3 instead of NUnit; the infrastructure
is the same, and so are the tests. One file in the test project, `TestFramework.cs`, knows the framework:
it defines the attributes the tests carry - `[Test]`, `[TestOf]`, `[Integration]`, `[RunsInParallel]`,
`[RunsAlone]` - on top of the framework's own, so a test has no `#if` and a change of framework touches
that file alone. `[Integration]` is an NUnit category and an xUnit trait named `TestCategory`, so the
filters `TestCategory=Integration` and `TestCategory!=Integration` select the same tests on both.

## A service test

A base class builds the context for one class under test: the class, its real repositories and the
`DbContext` on the in-memory provider, with mocks only for what leaves the service.

```csharp
public abstract class OrderServiceTestBase
{
    protected InMemoryTestExecutionContext<OrderService, OrdersDbContext> CreateTestContext()
    {
        var context = new InMemoryTestExecutionContext<OrderService, OrdersDbContext>();

        var mail = new Mock<IMailSender>();
        context.Register(mail);                    // the mock, to verify after the act
        context.Register(mail.Object);             // the service the class under test receives

        context.Register<IOrderRepository, OrderRepository>();
        context.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrdersDbContext>());
        return context;
    }
}
```

One fixture per method, in `OrderService.Place.Tests.cs`:

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

        var orderId = await ctx.ActAsync(s => s.PlaceAsync(new PlaceOrder(Customer.Id, Lines), default));

        await ctx.AssertAsync(async db => (await db.Orders.FindAsync(orderId)).ShouldNotBeNull());
    }
}
```

Each test gets its own database, so fixtures run in parallel. `ActAsync` and `AssertAsync` run in separate
scopes, so the assertion reads what was saved.

## Choosing a context

| Context | For |
|---|---|
| `TestExecutionContext` | a container alone |
| `ServiceTestExecutionContext<TService>` | a class without a database |
| `DbTestExecutionContext<TDbContext>` | a database without a class under test |
| `ServiceDbTestExecutionContext<TService, TDbContext>` | a class and a database on a provider you configure |
| `InMemoryTestExecutionContext<TService, TDbContext>` | a class and the in-memory database, with domain events wired |

## NUnit, xUnit and Shouldly

A service's tests are on NUnit by default and on xUnit v3 with `--TestFramework xunit`; `Common.Tests` stays
on NUnit. `Common.Testing`, which both reference, has no test framework: each in-memory test context names
its database with a new Guid, and each test project tells it how its framework skips a test through
`TestSkip.Handler`. Parallelism is the framework's own: NUnit runs a fixture marked
`[RunsInParallel]` alongside others, xUnit runs test classes in parallel and the tests of one class one
after another. NUnit builds a fixture for each test, as xUnit does, so a test sets up in the constructor
and cleans up in `Dispose` under both.

Assertions use Shouldly, which is MIT-licensed; FluentAssertions moved to a commercial licence with
version 8, which does not fit a template under MIT.

## Integration tests

What depends on PostgreSQL (transactions and the outbox, constraints, `ILIKE` and raw SQL, migrations)
runs against a real database in tests of the category `TestCategory=Integration`: `[Integration]` in a
service, `[Category(TestCategories.Integration)]` in `Common`. They read the
connection string from `TEST_POSTGRES` and are skipped, with that reason, when it is not set, so a plain
`dotnet test` needs no database:

```bash
eval "$(bash tests/servers/up.sh)"     # every test server of the solution, and its TEST_* variables
dotnet test --filter "TestCategory=Integration"
bash tests/servers/up.sh down          # removes the containers

dotnet test --filter "TestCategory!=Integration"   # everything else
```

Each server is a script in `tests/servers/` - `postgres.sh`, and one per flag that needs a server - with
`start`, `ready`, `logs` and `down`; `up.sh` runs every script it finds there, and CI runs the same scripts,
so a flag added later brings its script and changes nothing shared.

A solution generated with `--Database mssql` reads `TEST_SQLSERVER` instead, and its service tests use
`SqlServerDbTestExecutionContext` in place of `PostgresDbTestExecutionContext`. The service's schema is
migrated into one template database once per run, backed up, and each test restores the backup under a
name of its own, which is faster on SQL Server than migrating each test's database. `up.sh` starts SQL
Server for it.

`Common.Tests/Integration` has such fixtures, for numbers from a sequence and for object storage.

A test that registers domain events (`AddDomainEvents(...)` in `configure`) gets them on a real database
too: the integration contexts apply the domain event interceptors, and `ActAsync` publishes its scope to
them, as on the in-memory context. `PostgresDomainEventPhasesTests` and `SqlServerDomainEventPhasesTests`
check the phases after a single-statement save, a commit and a rollback there.

## Coverage

The test projects reference `coverlet.collector`, so coverage needs no setup:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Each test project writes a Cobertura report under `TestResults/`. With `--GitHubCiCd`, coverage is measured
on demand, not on every pull request: `coverage.yml` reports it per service and for `Common` over the unit
and integration runs, and can fail on a branch coverage threshold; see [CI](../features/ci.md).
