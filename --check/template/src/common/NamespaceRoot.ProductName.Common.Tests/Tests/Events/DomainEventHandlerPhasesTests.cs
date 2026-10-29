using NamespaceRoot.ProductName.Common.Application.Events.Handlers;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Events;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using NamespaceRoot.ProductName.Common.Tests.Stubs;
using Microsoft.Extensions.DependencyInjection;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Events;

/// <summary>
/// One Handle method cannot serve the phase before the save and the phase after the commit: such a handler
/// is refused when it is registered, not called twice at run time.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class DomainEventHandlerPhasesTests
{
    private sealed record Placed : IDomainEvent
    {
        public IDomainExecutionContext Context { get; } =
            new TestDomainExecutionContext(UserContextMockFactory.CreateSystemUser(), new TimeProviderMock(DateTimeOffset.UtcNow));
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class BothPhasesOfOneEvent : IDomainPreSaveHandler<Placed>, IDomainPostCommitHandler<Placed>
    {
        public Task Handle(Placed domainEvent, CancellationToken ct, object? data = null) => Task.CompletedTask;
    }

    private sealed class PreSaveAndRollback : IDomainPreSaveHandler<Placed>, IDomainRollbackHandler<Placed>
    {
        public Task Handle(Placed domainEvent, CancellationToken ct, object? data = null) => Task.CompletedTask;
        public Task HandleRollback(Placed domainEvent, Exception? exception, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Test]
    public void A_handler_of_one_event_before_the_save_and_after_the_commit_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddDomainEvents(typeof(BothPhasesOfOneEvent)))
            .Message.ShouldContain("Make it two classes");
    }

    [Test(Description = "A rollback handler has HandleRollback of its own, so it goes with either phase")]
    public void A_handler_before_the_save_and_on_rollback_is_accepted()
    {
        Should.NotThrow(() => new ServiceCollection().AddDomainEvents(typeof(PreSaveAndRollback)));
    }

}
