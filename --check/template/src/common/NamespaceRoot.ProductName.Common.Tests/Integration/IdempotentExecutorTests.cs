using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NamespaceRoot.ProductName.Common.Application.Idempotency;
using NamespaceRoot.ProductName.Common.Domain;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Idempotency;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Idempotency;
using NamespaceRoot.ProductName.Common.Tests.Stubs;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// A client that retries must not create a second thing. Checked on a real database, because the unique
/// index on the log is what decides a race, and nothing in memory behaves like it; each database the
/// template supports runs the same checks below.
/// </summary>
internal abstract class IdempotentExecutorTests
{
    private const string Operation = "widgets.create";
    private const string Key = "same-request-retried";

    private string _database = null!;
    private string _connection = null!;

    // --- the model: a widget whose name is unique, so its own violations can be told from the key's -----

    protected sealed class Widget : Entity<Guid>, IAggregateRoot
    {
        private Widget() { }

        public Widget(IDomainExecutionContext context, string name)
        {
            Id = Guid.NewGuid();
            Name = name;
            MarkCreated(context);
        }

        public string Name { get; private set; } = string.Empty;
    }

    protected sealed class Db(DbContextOptions<Db> options) : DbContextBase(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Widget>(widget =>
            {
                widget.ToTable("widgets");
                widget.HasIndex(w => w.Name).IsUnique();
            });
            modelBuilder.AddIdempotencyLog();
        }
    }

    private sealed record CreateWidget(string Name, string IdempotencyKey) : IIdempotentRequest;

    private sealed record WidgetCreated(Guid Id, string Name);

    // --- one database per test ------------------------------------------------------------------------

    [SetUp]
    public async Task CreateDatabase()
    {
        _database = $"idempotency_{Guid.NewGuid():N}";
        _connection = await CreateDatabaseAsync(_database);
        await using var db = NewDb();
        await db.Database.EnsureCreatedAsync();
    }

    [TearDown]
    public Task DropDatabase() => DropDatabaseAsync(_database);

    /// <summary>Creates an empty database and returns the connection string to it.</summary>
    protected abstract Task<string> CreateDatabaseAsync(string name);

    protected abstract Task DropDatabaseAsync(string name);

    protected abstract DbContextOptions<Db> Options(string connection);

    private Db NewDb() => new(Options(_connection));

    private static TestDomainExecutionContext Actor(Guid? tenant = null) =>
        new(new UserContextMock("11111111-1111-1111-1111-111111111111") { TenantId = tenant }, TimeProvider.System);

    private static IdempotentExecutor Executor(Db db, IDomainExecutionContext context) =>
        new(new EntityFrameworkIdempotencyLog<Db>(db), db, context, NullLogger<IdempotentExecutor>.Instance);

    /// <summary>A request handled the way a service handles one: its own scope, its own context.</summary>
    private async Task<WidgetCreated> CreateAsync(CreateWidget request, IDomainExecutionContext? context = null)
    {
        context ??= Actor();
        await using var db = NewDb();
        return await Executor(db, context).ExecuteAsync(request, Operation, _ =>
        {
            var widget = new Widget(context, request.Name);
            db.Add(widget);
            return Task.FromResult(new WidgetCreated(widget.Id, widget.Name));
        });
    }

    private async Task<int> WidgetsAsync()
    {
        await using var db = NewDb();
        return await db.Set<Widget>().CountAsync();
    }

    // --- behaviour ------------------------------------------------------------------------------------

    [Test]
    public async Task A_retry_gets_the_first_answer_and_creates_nothing_more()
    {
        var first = await CreateAsync(new CreateWidget("a", Key));
        var retry = await CreateAsync(new CreateWidget("a", Key));

        retry.ShouldBe(first);
        (await WidgetsAsync()).ShouldBe(1);
    }

    [Test]
    public async Task Two_requests_racing_with_one_key_create_one_thing_and_get_one_answer()
    {
        // Both find nothing, both do the work, then both commit: the index lets one through.
        var bothWorking = new TaskCompletionSource();
        var arrived = 0;
        var context = Actor();
        await using var secondDb = NewDb();

        async Task<WidgetCreated> Racer(Db db, string name) =>
            await Executor(db, context).ExecuteAsync(new CreateWidget(name, Key), Operation, async _ =>
            {
                var widget = new Widget(context, name);
                db.Add(widget);
                if (Interlocked.Increment(ref arrived) == 2) bothWorking.SetResult();
                await bothWorking.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return new WidgetCreated(widget.Id, widget.Name);
            });

        await using var firstDb = NewDb();
        var answers = await Task.WhenAll(Racer(firstDb, "a"), Racer(secondDb, "b"));

        answers[0].ShouldBe(answers[1], "the one that lost answers with what the winner recorded");
        (await WidgetsAsync()).ShouldBe(1);

        // What the one that lost tracked went with its rollback: another save in either scope inserts nothing.
        await firstDb.SaveChangesAsync();
        await secondDb.SaveChangesAsync();
        (await WidgetsAsync()).ShouldBe(1);
    }

    [Test]
    public async Task A_key_out_of_bounds_is_refused()
    {
        await Should.ThrowAsync<InconsistentDataException>(() => CreateAsync(new CreateWidget("a", "short")));
        (await WidgetsAsync()).ShouldBe(0);
    }

    [Test]
    public async Task A_key_reused_for_another_operation_is_refused()
    {
        await CreateAsync(new CreateWidget("a", Key));

        await using var db = NewDb();
        var refused = await Should.ThrowAsync<ConflictException>(() =>
            Executor(db, Actor()).ExecuteAsync(new CreateWidget("b", Key), "widgets.rename", _ => Task.FromResult(0)));

        refused.ErrorCode.ShouldBe(IdempotentExecutor.KeyReusedCode);
    }

    [Test]
    public async Task Work_whose_answer_is_not_kept_refuses_a_repeat()
    {
        async Task<string> IssueAsync()
        {
            await using var db = NewDb();
            return await Executor(db, Actor()).ExecuteOnceAsync(new CreateWidget("a", Key), "secrets.issue",
                _ => Task.FromResult("secret-shown-once"));
        }

        (await IssueAsync()).ShouldBe("secret-shown-once");
        var refused = await Should.ThrowAsync<ConflictException>(IssueAsync);
        refused.ErrorCode.ShouldBe(IdempotentExecutor.AlreadyCarriedOutCode);

        await using var check = NewDb();
        (await check.Set<IdempotencyRecord>().SingleAsync()).Response.ShouldBeEmpty("the secret is kept nowhere");
    }

    [Test]
    public async Task A_failed_attempt_records_nothing_and_the_retry_does_the_work()
    {
        await using (var db = NewDb())
        {
            await Should.ThrowAsync<InvalidOperationException>(() =>
                Executor(db, Actor()).ExecuteAsync<WidgetCreated>(new CreateWidget("a", Key), Operation,
                    _ => throw new InvalidOperationException("the provider is down")));
        }

        var retry = await CreateAsync(new CreateWidget("a", Key));

        retry.Name.ShouldBe("a");
        (await WidgetsAsync()).ShouldBe(1);
    }

    [Test]
    public async Task A_duplicate_the_work_itself_refuses_is_not_taken_for_a_race()
    {
        await CreateAsync(new CreateWidget("a", "key-aaaaaaaaaaaaaaaa"));

        // Another key, the same widget name: the widget's own index refuses it. A retry would fail the
        // same way, so the caller hears the real reason, not "retry with the same key".
        await Should.ThrowAsync<UniqueViolationException>(() =>
            CreateAsync(new CreateWidget("a", "key-bbbbbbbbbbbbbbbb")));
    }

    [Test]
    public async Task One_key_in_two_tenants_is_two_requests()
    {
        var first = await CreateAsync(new CreateWidget("a", Key), Actor(Guid.NewGuid()));
        var second = await CreateAsync(new CreateWidget("b", Key), Actor(Guid.NewGuid()));

        second.ShouldNotBe(first);
        (await WidgetsAsync()).ShouldBe(2);
    }
}
