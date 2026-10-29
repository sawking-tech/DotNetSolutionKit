using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Application.Events.Handlers;

/// <summary>
/// Internal non-generic interface to allow high-performance dispatching
/// without reflection or dynamic overhead.
/// </summary>
public interface IDomainEventHandler
{
    Task Handle(IDomainEvent domainEvent, CancellationToken ct, object? data = null);
}

public interface IDomainEventHandler<in TEvent> : IDomainEventHandler
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct, object? data = null);

    // Explicit implementation to bridge the call to the typed Handle
    Task IDomainEventHandler.Handle(IDomainEvent e, CancellationToken ct, object? data)
        => Handle((TEvent)e, ct, data);
}
