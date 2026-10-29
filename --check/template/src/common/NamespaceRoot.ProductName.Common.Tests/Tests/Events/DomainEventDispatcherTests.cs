using NamespaceRoot.ProductName.Common.Application.Events;
using NamespaceRoot.ProductName.Common.Application.Events.Handlers;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Events;
using NamespaceRoot.ProductName.Common.Tests.Stubs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Events;

[TestFixture]
[TestOf(typeof(DomainEventDispatcher))]
[Parallelizable(ParallelScope.All)]
public class DomainEventDispatcherTests
{
    [Test]
    [Description("PreSave handler exception propagates so the surrounding transaction can roll back")]
    public async Task DispatchPreSave_HandlerThrows_ExceptionPropagates()
    {
        var (dispatcher, _, _) = Build(preSaveHandler: new ThrowingPreSaveHandler());
        var evt = new TestEvent();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => dispatcher.DispatchPreSaveAsync([evt]));

        ex.Message.ShouldBe("pre-save boom");
    }

    [Test]
    [Description("PostCommit handler exception is logged at Error level and swallowed (transaction already committed)")]
    public async Task DispatchPostCommit_HandlerThrows_IsLoggedAndSwallowed()
    {
        var logger = new Mock<ILogger<DomainEventDispatcher>>();
        var (dispatcher, _, _) = Build(postCommitHandler: new ThrowingPostCommitHandler(), logger: logger.Object);

        await dispatcher.DispatchPostCommitAsync([new TestEvent()]);

        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v != null && v.ToString()!.Contains("ThrowingPostCommitHandler")
                                              && v.ToString()!.Contains("PostCommit")
                                              && v.ToString()!.Contains(nameof(TestEvent))),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    [Description("Rollback handler exception is logged at Error level and swallowed (transaction already rolling back)")]
    public async Task DispatchRollback_HandlerThrows_IsLoggedAndSwallowed()
    {
        var logger = new Mock<ILogger<DomainEventDispatcher>>();
        var (dispatcher, _, _) = Build(rollbackHandler: new ThrowingRollbackHandler(), logger: logger.Object);

        await dispatcher.DispatchRollbackAsync([new TestEvent()], exception: new InvalidOperationException("cause"));

        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v != null && v.ToString()!.Contains("ThrowingRollbackHandler")
                                              && v.ToString()!.Contains("Rollback")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    [Description("All handlers for a phase run even when one of them throws")]
    public async Task DispatchPostCommit_OneHandlerThrows_OtherStillRuns()
    {
        var good = new RecordingPostCommitHandler();
        var services = new ServiceCollection();
        services.AddScoped<IDomainPostCommitHandler<TestEvent>>(_ => new ThrowingPostCommitHandler());
        services.AddScoped<IDomainPostCommitHandler<TestEvent>>(_ => good);
        var sp = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(
            sp, sp.GetRequiredService<IServiceScopeFactory>(), Mock.Of<ILogger<DomainEventDispatcher>>());

        await dispatcher.DispatchPostCommitAsync([new TestEvent()]);

        good.Calls.ShouldBe(1);
    }

    [Test]
    [Description("PostCommit handlers run in a FRESH DI scope per event - scoped dependencies are not shared with the ambient scope, so the handler can open its own transaction and use its own outbox window")]
    public async Task DispatchPostCommit_ScopedDependency_ResolvedFromFreshScopePerEvent()
    {
        var seenProbes = new List<ScopeProbe>();
        var services = new ServiceCollection();
        services.AddScoped<ScopeProbe>();
        services.AddScoped<IDomainPostCommitHandler<TestEvent>>(sp2 =>
            new ProbeCapturingPostCommitHandler(sp2.GetRequiredService<ScopeProbe>(), seenProbes));
        var sp = services.BuildServiceProvider();
        using var ambientScope = sp.CreateScope();
        var ambientProbe = ambientScope.ServiceProvider.GetRequiredService<ScopeProbe>();
        var dispatcher = new DomainEventDispatcher(
            ambientScope.ServiceProvider,
            sp.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<DomainEventDispatcher>>());

        await dispatcher.DispatchPostCommitAsync([new TestEvent(), new TestEvent()]);

        seenProbes.Count.ShouldBe(2);
        // Not the ambient scope's instance...
        seenProbes.ShouldAllBe(p => !ReferenceEquals(p, ambientProbe));
        // ...and each event got its own scope.
        ReferenceEquals(seenProbes[0], seenProbes[1]).ShouldBeFalse();
    }

    [Test]
    [Description("PreSave handlers stay in the AMBIENT scope - same DbContext, same open transaction as the triggering write")]
    public async Task DispatchPreSave_ScopedDependency_ResolvedFromAmbientScope()
    {
        var seenProbes = new List<ScopeProbe>();
        var services = new ServiceCollection();
        services.AddScoped<ScopeProbe>();
        services.AddScoped<IDomainPreSaveHandler<TestEvent>>(sp2 =>
            new ProbeCapturingPreSaveHandler(sp2.GetRequiredService<ScopeProbe>(), seenProbes));
        var sp = services.BuildServiceProvider();
        using var ambientScope = sp.CreateScope();
        var ambientProbe = ambientScope.ServiceProvider.GetRequiredService<ScopeProbe>();
        var dispatcher = new DomainEventDispatcher(
            ambientScope.ServiceProvider,
            sp.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<DomainEventDispatcher>>());

        await dispatcher.DispatchPreSaveAsync([new TestEvent()]);

        seenProbes.Count.ShouldBe(1);
        ReferenceEquals(seenProbes[0], ambientProbe).ShouldBeTrue();
    }

    // --- Setup ---

    private static (DomainEventDispatcher Dispatcher, IServiceProvider Sp, RecordingPreSaveHandler? Probe) Build(
        IDomainPreSaveHandler<TestEvent>? preSaveHandler = null,
        IDomainPostCommitHandler<TestEvent>? postCommitHandler = null,
        IDomainRollbackHandler<TestEvent>? rollbackHandler = null,
        ILogger<DomainEventDispatcher>? logger = null)
    {
        var services = new ServiceCollection();
        if (preSaveHandler is not null)
            services.AddScoped<IDomainPreSaveHandler<TestEvent>>(_ => preSaveHandler);
        if (postCommitHandler is not null)
            services.AddScoped<IDomainPostCommitHandler<TestEvent>>(_ => postCommitHandler);
        if (rollbackHandler is not null)
            services.AddScoped<IDomainRollbackHandler<TestEvent>>(_ => rollbackHandler);
        var sp = services.BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(
            sp, sp.GetRequiredService<IServiceScopeFactory>(), logger ?? Mock.Of<ILogger<DomainEventDispatcher>>());
        return (dispatcher, sp, preSaveHandler as RecordingPreSaveHandler);
    }

    private sealed class ScopeProbe;

    private sealed class ProbeCapturingPostCommitHandler(ScopeProbe probe, List<ScopeProbe> seen)
        : IDomainPostCommitHandler<TestEvent>
    {
        public Task Handle(TestEvent @event, CancellationToken ct, object? data = null)
        {
            seen.Add(probe);
            return Task.CompletedTask;
        }
    }

    private sealed class ProbeCapturingPreSaveHandler(ScopeProbe probe, List<ScopeProbe> seen)
        : IDomainPreSaveHandler<TestEvent>
    {
        public Task Handle(TestEvent @event, CancellationToken ct, object? data = null)
        {
            seen.Add(probe);
            return Task.CompletedTask;
        }
    }

    private sealed record TestEvent : IDomainEvent
    {
        public IDomainExecutionContext Context { get; } =
            new TestDomainExecutionContext(UserContextMockFactory.CreateSystemUser(), new TimeProviderMock(DateTimeOffset.UtcNow));
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class ThrowingPreSaveHandler : IDomainPreSaveHandler<TestEvent>
    {
        public Task Handle(TestEvent @event, CancellationToken ct, object? data = null)
            => throw new InvalidOperationException("pre-save boom");
    }

    private sealed class ThrowingPostCommitHandler : IDomainPostCommitHandler<TestEvent>
    {
        public Task Handle(TestEvent @event, CancellationToken ct, object? data = null)
            => throw new InvalidOperationException("post-commit boom");
    }

    private sealed class ThrowingRollbackHandler : IDomainRollbackHandler<TestEvent>
    {
        public Task HandleRollback(TestEvent domainEvent, Exception? exception, CancellationToken cancellationToken)
            => throw new InvalidOperationException("rollback boom");
    }

    private sealed class RecordingPreSaveHandler : IDomainPreSaveHandler<TestEvent>
    {
        public int Calls;
        public Task Handle(TestEvent @event, CancellationToken ct, object? data = null)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPostCommitHandler : IDomainPostCommitHandler<TestEvent>
    {
        public int Calls;
        public Task Handle(TestEvent @event, CancellationToken ct, object? data = null)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
