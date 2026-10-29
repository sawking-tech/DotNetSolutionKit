using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Domain;

/// <summary>
/// Entity that can raise domain events but is not an aggregate root.
/// Use for entities that need event support but are owned or managed within a service boundary.
/// The infrastructure event pipeline treats <see cref="EventfulEntity{TId}"/> the same as
/// <see cref="AggregateRoot{TId}"/> - events are dispatched through the same three-phase pipeline.
/// </summary>
public abstract class EventfulEntity<TId> : Entity<TId>, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Domain events raised by this entity.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>Explicit implementation to hide infrastructure cleanup from the public API.</summary>
    void IHasDomainEvents.ClearDomainEvents() => _domainEvents.Clear();
}
