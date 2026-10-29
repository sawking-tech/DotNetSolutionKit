namespace NamespaceRoot.ProductName.Common.Domain.Events;

/// <summary>
/// An entity whose raised events the persistence layer can collect and clear.
/// </summary>
/// <remarks>
/// Separate from the entity's own API on purpose: collecting and clearing events is the persistence
/// layer's business, and an entity that offered <c>ClearDomainEvents()</c> among its business methods
/// would invite somebody to call it.
/// </remarks>
public interface IHasDomainEvents
{
    /// <summary>
    /// Events raised and not yet dispatched.
    /// </summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>
    /// Forgets the raised events. Called once they have been taken for dispatch.
    /// </summary>
    void ClearDomainEvents();
}
