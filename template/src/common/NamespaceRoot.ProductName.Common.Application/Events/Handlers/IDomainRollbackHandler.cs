using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Application.Events.Handlers;

/// <summary>
/// Handler executed if the transaction is rolled back due to an error.
/// Use for cleanup, logging, or compensating actions.
/// </summary>
/// <remarks><b>Lifetime:</b> Registered as <b>Scoped</b>. A new instance is created per request/scope.</remarks>
/// <typeparam name="TEvent">Type of domain event this handler processes.</typeparam>
public interface IDomainRollbackHandler<in TEvent>
    : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the rollback logic. The exception may be null if the rollback
    /// was triggered manually without a specific error.
    /// </summary>
    /// <param name="domainEvent">The domain event instance.</param>
    /// <param name="exception">The exception that triggered the rollback, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleRollback(TEvent domainEvent, Exception? exception, CancellationToken cancellationToken);

    /// <summary>
    /// Redirects the base handle call to the specialized <see cref="HandleRollback"/> method.
    /// </summary>
    Task IDomainEventHandler<TEvent>.Handle(TEvent domainEvent, CancellationToken ct, object? data)
    {
        // We pass data as Exception if it's there, otherwise null.
        return HandleRollback(domainEvent, data as Exception, ct);
    }
}
