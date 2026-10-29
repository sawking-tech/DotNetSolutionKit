using JetBrains.Annotations;
using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Domain.Events;

/// <summary>
/// Marker interface representing a domain event within the system.
/// All domain events must implement this interface to be dispatched
/// through the domain event handling pipeline.
/// </summary>
[UsedImplicitly]
public interface IDomainEvent
{
    /// <summary>
    /// Execution context in which the event occurred, including actor and time provider.
    /// Never null; for system events, the context can contain a System actor.
    /// </summary>
    IDomainExecutionContext Context { get; }

    /// <summary>
    /// UTC timestamp indicating the exact moment when the event was raised.
    /// Provides consistent timing for logging, auditing, and event handling.
    /// </summary>
    DateTimeOffset OccurredAt { get; }
}
