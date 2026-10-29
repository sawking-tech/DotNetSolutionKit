using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Events.Handlers;
using NamespaceRoot.ProductName.Common.Domain;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Events;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;
using NamespaceRoot.ProductName.Common.Tests.Stubs;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Events;

/// <summary>
/// Every way a write becomes final runs the phase after it, once: a save with no transaction around it
/// (which EF Core sends without a transaction at all), a commit, a rollback - on the in-memory provider of
/// the service tests, where no transaction event ever fires.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class DomainEventPhasesOnSaveTests
{
    public sealed record Created(IDomainExecutionContext Context, Guid Id) : IDomainEvent
    {
        public DateTimeOffset OccurredAt { get; init; } = Context.TimeProvider.GetUtcNow();
    }

    public sealed class Thing : AggregateRoot<Guid>
    {
        private Thing() { }

        public Thing(IDomainExecutionContext context)
        {
            Id = Guid.NewGuid();
            AddDomainEvent(new Created(context, Id));
        }
    }

    public sealed class ThingsDb(DbContextOptions<ThingsDb> options) : DbContextBase(options)
    {
        public DbSet<Thing> Things => Set<Thing>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Thing>().Ignore(t => t.DomainEvents);
    }

    public sealed class Phases
    {
        public List<string> Seen { get; } = [];
    }

    public sealed class BeforeSave(Phases phases) : IDomainPreSaveHandler<Created>
    {
        public Task Handle(Created e, CancellationToken ct, object? data = null)
        {
            phases.Seen.Add("pre-save");
            return Task.CompletedTask;
        }
    }

    public sealed class AfterCommit(Phases phases) : IDomainPostCommitHandler<Created>
    {
        public Task Handle(Created e, CancellationToken ct, object? data = null)
        {
            phases.Seen.Add("post-commit");
            return Task.CompletedTask;
        }
    }

    public sealed class OnRollback(Phases phases) : IDomainRollbackHandler<Created>
    {
        public Task HandleRollback(Created e, Exception? exception, CancellationToken ct)
        {
            phases.Seen.Add("rollback");
            return Task.CompletedTask;
        }
    }

    public sealed class Writes(ThingsDb db)
    {
        private static readonly IDomainExecutionContext Actor =
            new TestDomainExecutionContext(new UserContextMock(), TimeProvider.System);

        public async Task SaveAsync()
        {
            db.Things.Add(new Thing(Actor));
            await db.SaveChangesAsync();
        }

        public async Task CommitAsync()
        {
            await db.BeginTransactionAsync();
            db.Things.Add(new Thing(Actor));
            await db.CommitTransactionAsync();
        }

        public async Task RollBackAsync()
        {
            await db.BeginTransactionAsync();
            db.Things.Add(new Thing(Actor));
            await db.SaveChangesAsync();
            await db.RollbackTransactionAsync();
        }
    }

    private static InMemoryTestExecutionContext<Writes, ThingsDb> Context(Phases phases)
    {
        var context = new InMemoryTestExecutionContext<Writes, ThingsDb>();
        context.Register(phases);
        context.Services.AddLogging();
        context.Services.AddDomainEvents(typeof(BeforeSave), typeof(AfterCommit), typeof(OnRollback));
        return context;
    }

    [Test(Description = "A save with no transaction around it runs the post-commit phase once it returns")]
    public async Task Should_RunPostCommit_When_ASaveCommitsOnItsOwn()
    {
        var phases = new Phases();
        await using var ctx = Context(phases);

        await ctx.ActAsync(w => w.SaveAsync());

        phases.Seen.ShouldBe(["pre-save", "post-commit"]);
    }

    [Test(Description = "A commit runs the post-commit phase once, after the save inside it")]
    public async Task Should_RunPostCommitOnce_When_ATransactionCommits()
    {
        var phases = new Phases();
        await using var ctx = Context(phases);

        await ctx.ActAsync(w => w.CommitAsync());

        phases.Seen.ShouldBe(["pre-save", "post-commit"]);
    }

    [Test(Description = "A rollback runs the rollback phase, and no post-commit")]
    public async Task Should_RunRollback_When_ATransactionRollsBack()
    {
        var phases = new Phases();
        await using var ctx = Context(phases);

        await ctx.ActAsync(w => w.RollBackAsync());

        phases.Seen.ShouldBe(["pre-save", "rollback"]);
    }
}
