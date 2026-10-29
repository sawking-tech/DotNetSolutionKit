namespace NamespaceRoot.ProductName.Common.Domain;

/// <summary>
/// Base class for business entities that act as Aggregate Roots and support domain events.
/// </summary>
public abstract class AggregateRoot<TId> : EventfulEntity<TId>, IAggregateRoot
{
}
