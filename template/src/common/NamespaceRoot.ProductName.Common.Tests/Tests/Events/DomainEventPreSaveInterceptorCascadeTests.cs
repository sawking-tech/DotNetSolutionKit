using NamespaceRoot.ProductName.Common.Application.Events;
using NamespaceRoot.ProductName.Common.Application.Events.Handlers;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain;
using NamespaceRoot.ProductName.Common.Domain.Events;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using NamespaceRoot.ProductName.Common.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Events;

/// <summary>
/// Cascade harvesting tests for <see cref="DomainEventPreSaveInterceptor"/>: a PreSave handler
/// that creates a new aggregate must have that aggregate's domain events reach
/// <c>IDomainEventStorage</c> and dispatched through PreSave / PostCommit. Kept as a regression guard: this cascade stopped working once.
/// </summary>
[TestFixture]
[TestOf(typeof(DomainEventPreSaveInterceptor))]
[Parallelizable(ParallelScope.All)]
public class DomainEventPreSaveInterceptorCascadeTests
{
    [Test]
    [Description("PreSave handler that adds a new aggregate - both the original and the cascaded event reach storage and dispatch")]
    public async Task SaveChanges_CascadeEvent_BothEventsReachStorageAndDispatch()
    {
        await using var ctx = new CascadeContext();
        var preSaveCalls = new List<string>();
        ctx.Services.AddScoped<IDomainPreSaveHandler<RootCreatedEvent>>(_ =>
            new AddsChildPreSaveHandler(preSaveCalls));
        ctx.Services.AddScoped<IDomainPreSaveHandler<ChildCreatedEvent>>(_ =>
            new RecordingPreSaveHandler<ChildCreatedEvent>(preSaveCalls));
        var postCommitCalls = new List<string>();
        ctx.Services.AddScoped<IDomainPostCommitHandler<RootCreatedEvent>>(_ =>
            new RecordingPostCommitHandler<RootCreatedEvent>(postCommitCalls));
        ctx.Services.AddScoped<IDomainPostCommitHandler<ChildCreatedEvent>>(_ =>
            new RecordingPostCommitHandler<ChildCreatedEvent>(postCommitCalls));

        IServiceProvider? scopeSp = null;
        await ctx.ExecuteAsync<TestDbContext>(
            async db =>
            {
                using (DomainEventScopeContext.Use(scopeSp!))
                {
                    db.Roots.Add(new RootEntity());
                    await db.SaveChangesAsync();
                }
            },
            configure: sp => scopeSp = sp);

        // Both events reach the post-commit phase - the bug was that ChildCreatedEvent stayed on the
        // entity and never reached storage, so nothing after the save saw it.
        postCommitCalls.ShouldContain(nameof(RootCreatedEvent));
        postCommitCalls.ShouldContain(nameof(ChildCreatedEvent));

        // PreSave handlers should have fired for both events.
        preSaveCalls.ShouldContain(nameof(RootCreatedEvent));
        preSaveCalls.ShouldContain(nameof(ChildCreatedEvent));
    }

    [Test]
    [Description("Runaway PreSave handler that keeps emitting new events on every pass - interceptor fails fast with a clear cascade-limit error")]
    public async Task SaveChanges_RunawayCascade_ThrowsWithCascadeLimitMessage()
    {
        await using var ctx = new RunawayContext();

        IServiceProvider? scopeSp = null;
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            ctx.ExecuteAsync<TestDbContext>(
                async db =>
                {
                    using (DomainEventScopeContext.Use(scopeSp!))
                    {
                        db.Roots.Add(new RootEntity());
                        await db.SaveChangesAsync();
                    }
                },
                configure: sp => scopeSp = sp));

        ex.Message.ShouldContain("cascade");
    }

    // --- Test entities + events ---

    private sealed class RootEntity : IHasDomainEvents
    {
        public Guid Id { get; } = Guid.NewGuid();
        private readonly List<IDomainEvent> _events = [new RootCreatedEvent()];
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;
        public void ClearDomainEvents() => _events.Clear();
    }

    private sealed class ChildEntity : IHasDomainEvents
    {
        public Guid Id { get; } = Guid.NewGuid();
        private readonly List<IDomainEvent> _events = [new ChildCreatedEvent()];
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;
        public void ClearDomainEvents() => _events.Clear();
    }

    private sealed record RootCreatedEvent : IDomainEvent
    {
        public IDomainExecutionContext Context { get; } =
            new TestDomainExecutionContext(UserContextMockFactory.CreateSystemUser(), new TimeProviderMock(DateTimeOffset.UtcNow));
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed record ChildCreatedEvent : IDomainEvent
    {
        public IDomainExecutionContext Context { get; } =
            new TestDomainExecutionContext(UserContextMockFactory.CreateSystemUser(), new TimeProviderMock(DateTimeOffset.UtcNow));
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<RootEntity> Roots => Set<RootEntity>();
        public DbSet<ChildEntity> Children => Set<ChildEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RootEntity>(b =>
            {
                b.HasKey(e => e.Id);
                b.Ignore(e => e.DomainEvents);
            });
            modelBuilder.Entity<ChildEntity>(b =>
            {
                b.HasKey(e => e.Id);
                b.Ignore(e => e.DomainEvents);
            });
        }
    }

    // --- Handlers ---

    private sealed class AddsChildPreSaveHandler(List<string> calls) : IDomainPreSaveHandler<RootCreatedEvent>
    {
        public Task Handle(RootCreatedEvent @event, CancellationToken ct, object? data = null)
        {
            calls.Add(nameof(RootCreatedEvent));
            // Cascade: add a new aggregate with its own event while we're inside PreSave.
            // Without the interceptor's fixed-point loop this child event would never be harvested.
            var db = DomainEventScopeContext.Current!.GetRequiredService<TestDbContext>();
            db.Children.Add(new ChildEntity());
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPreSaveHandler<T>(List<string> calls) : IDomainPreSaveHandler<T>
        where T : IDomainEvent
    {
        public Task Handle(T @event, CancellationToken ct, object? data = null)
        {
            calls.Add(typeof(T).Name);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPostCommitHandler<T>(List<string> calls) : IDomainPostCommitHandler<T>
        where T : IDomainEvent
    {
        public Task Handle(T @event, CancellationToken ct, object? data = null)
        {
            calls.Add(typeof(T).Name);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Always attaches a fresh child entity with its own event - interceptor must detect
    /// the non-converging cascade and throw before exhausting memory.
    /// </summary>
    private sealed class RunawayRootPreSaveHandler : IDomainPreSaveHandler<RootCreatedEvent>
    {
        public Task Handle(RootCreatedEvent @event, CancellationToken ct, object? data = null)
        {
            DomainEventScopeContext.Current!.GetRequiredService<TestDbContext>().Children.Add(new ChildEntity());
            return Task.CompletedTask;
        }
    }

    private sealed class RunawayChildPreSaveHandler : IDomainPreSaveHandler<ChildCreatedEvent>
    {
        public Task Handle(ChildCreatedEvent @event, CancellationToken ct, object? data = null)
        {
            DomainEventScopeContext.Current!.GetRequiredService<TestDbContext>().Children.Add(new ChildEntity());
            return Task.CompletedTask;
        }
    }

    // --- Test contexts ---

    private sealed class CascadeContext : DbTestExecutionContext<TestDbContext>
    {
        public CascadeContext()
        {
            Services.AddDomainEventCore();
            Services.AddDomainEventPersistence();
            Services.AddDbContext<TestDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase($"Cascade_{Guid.NewGuid()}");
                options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
                options.ApplyDomainEventInterceptors(sp);
            });
        }
    }

    private sealed class RunawayContext : DbTestExecutionContext<TestDbContext>
    {
        public RunawayContext()
        {
            Services.AddDomainEventCore();
            Services.AddDomainEventPersistence();
            Services.AddScoped<IDomainPreSaveHandler<RootCreatedEvent>, RunawayRootPreSaveHandler>();
            Services.AddScoped<IDomainPreSaveHandler<ChildCreatedEvent>, RunawayChildPreSaveHandler>();
            Services.AddDbContext<TestDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase($"Runaway_{Guid.NewGuid()}");
                options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
                options.ApplyDomainEventInterceptors(sp);
            });
        }
    }
}
