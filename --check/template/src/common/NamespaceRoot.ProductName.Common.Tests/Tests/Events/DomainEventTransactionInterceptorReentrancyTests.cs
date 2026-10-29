using NamespaceRoot.ProductName.Common.Application.Events;
using NamespaceRoot.ProductName.Common.Application.Events.Handlers;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Events;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using NamespaceRoot.ProductName.Common.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Events;

/// <summary>
/// Reentrancy guard tests for <see cref="DomainEventTransactionInterceptor"/>: a PostCommit
/// handler that commits its own transaction on another DbContext in the SAME scope re-enters
/// the (singleton) interceptor, which resolves the SAME scoped storage. Without the guard the
/// nested commit re-dispatched the outer events and Clear()ed the live list the outer dispatch
/// was still iterating. Regression for the credit-monitoring payment-confirm 500
/// («This NpgsqlTransaction has completed») found 2026-07-21.
/// </summary>
[TestFixture]
[TestOf(typeof(DomainEventTransactionInterceptor))]
[Parallelizable(ParallelScope.All)]
public class DomainEventTransactionInterceptorReentrancyTests
{
    [Test(Description = "Nested same-scope commit inside a PostCommit handler must not re-dispatch the outer events or clear them mid-iteration - each handler fires exactly once and storage ends empty")]
    public async Task Should_DispatchEachEventOnce_When_HandlerTriggersNestedCommitInSameScope()
    {
        var calls = new List<string>();
        var interceptor = new DomainEventTransactionInterceptor();

        await using var db = new ResolverDbContext(
            new DbContextOptionsBuilder<ResolverDbContext>()
                .UseInMemoryDatabase($"Reentrancy_{Guid.NewGuid()}")
                .Options);

        var eventData = new TransactionEndEventData(
            eventDefinition: null!,
            messageGenerator: null!,
            transaction: null!,
            context: db,
            transactionId: Guid.NewGuid(),
            connectionId: Guid.NewGuid(),
            async: true,
            startTime: DateTimeOffset.UtcNow,
            duration: TimeSpan.Zero);

        var services = new ServiceCollection();
        services.AddDomainEventCore();
        services.AddScoped<IDomainPostCommitHandler<FirstEvent>>(_ =>
            new NestedCommitPostCommitHandler(calls, () => interceptor.TransactionCommittedAsync(null!, eventData)));
        services.AddScoped<IDomainPostCommitHandler<SecondEvent>>(_ =>
            new RecordingPostCommitHandler<SecondEvent>(calls));
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var storage = scope.ServiceProvider.GetRequiredService<IDomainEventStorage>();
        storage.AddEvents([new FirstEvent(), new SecondEvent()]);

        using (DomainEventScopeContext.Use(scope.ServiceProvider))
        {
            await interceptor.TransactionCommittedAsync(null!, eventData, CancellationToken.None);
        }

        // Each event dispatched exactly once - the nested call skipped dispatch entirely.
        calls.ShouldBe([nameof(FirstEvent), nameof(SecondEvent)]);
        storage.GetEvents().ShouldBeEmpty();
    }

    [Test(Description = "Nested same-scope rollback inside a Rollback handler must not re-dispatch or clear the outer events mid-iteration")]
    public async Task Should_DispatchEachEventOnce_When_HandlerTriggersNestedRollbackInSameScope()
    {
        var calls = new List<string>();
        var interceptor = new DomainEventTransactionInterceptor();

        await using var db = new ResolverDbContext(
            new DbContextOptionsBuilder<ResolverDbContext>()
                .UseInMemoryDatabase($"ReentrancyRb_{Guid.NewGuid()}")
                .Options);

        var eventData = new TransactionEndEventData(
            eventDefinition: null!,
            messageGenerator: null!,
            transaction: null!,
            context: db,
            transactionId: Guid.NewGuid(),
            connectionId: Guid.NewGuid(),
            async: true,
            startTime: DateTimeOffset.UtcNow,
            duration: TimeSpan.Zero);

        var services = new ServiceCollection();
        services.AddDomainEventCore();
        services.AddScoped<IDomainRollbackHandler<FirstEvent>>(_ =>
            new NestedRollbackHandler(calls, () => interceptor.TransactionRolledBackAsync(null!, eventData)));
        services.AddScoped<IDomainRollbackHandler<SecondEvent>>(_ =>
            new RecordingRollbackHandler<SecondEvent>(calls));
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var storage = scope.ServiceProvider.GetRequiredService<IDomainEventStorage>();
        storage.AddEvents([new FirstEvent(), new SecondEvent()]);

        using (DomainEventScopeContext.Use(scope.ServiceProvider))
        {
            await interceptor.TransactionRolledBackAsync(null!, eventData, CancellationToken.None);
        }

        calls.ShouldBe([nameof(FirstEvent), nameof(SecondEvent)]);
        storage.GetEvents().ShouldBeEmpty();
    }

    // --- Test events ---

    private sealed record FirstEvent : IDomainEvent
    {
        public IDomainExecutionContext Context { get; } =
            new TestDomainExecutionContext(UserContextMockFactory.CreateSystemUser(), new TimeProviderMock(DateTimeOffset.UtcNow));
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed record SecondEvent : IDomainEvent
    {
        public IDomainExecutionContext Context { get; } =
            new TestDomainExecutionContext(UserContextMockFactory.CreateSystemUser(), new TimeProviderMock(DateTimeOffset.UtcNow));
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    // Bare context whose only job is to satisfy DomainEventInfrastructureResolver's
    // internal-service-provider lookup on eventData.Context.
    private sealed class ResolverDbContext(DbContextOptions<ResolverDbContext> options) : DbContext(options);

    // --- Handlers ---

    private sealed class NestedCommitPostCommitHandler(List<string> calls, Func<Task> nestedCommit)
        : IDomainPostCommitHandler<FirstEvent>
    {
        public async Task Handle(FirstEvent @event, CancellationToken ct, object? data = null)
        {
            calls.Add(nameof(FirstEvent));
            // Simulates a handler committing its own transaction on another DbContext in the
            // same scope - the singleton interceptor fires again with the same scoped storage.
            await nestedCommit();
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

    private sealed class NestedRollbackHandler(List<string> calls, Func<Task> nestedRollback)
        : IDomainRollbackHandler<FirstEvent>
    {
        public async Task HandleRollback(FirstEvent domainEvent, Exception? exception, CancellationToken cancellationToken)
        {
            calls.Add(nameof(FirstEvent));
            await nestedRollback();
        }
    }

    private sealed class RecordingRollbackHandler<T>(List<string> calls) : IDomainRollbackHandler<T>
        where T : IDomainEvent
    {
        public Task HandleRollback(T domainEvent, Exception? exception, CancellationToken cancellationToken)
        {
            calls.Add(typeof(T).Name);
            return Task.CompletedTask;
        }
    }
}
