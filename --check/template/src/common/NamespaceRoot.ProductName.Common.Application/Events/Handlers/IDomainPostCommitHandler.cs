using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Application.Events.Handlers;

/// <summary>
/// Handler executed after the transaction has been successfully committed.
/// Use for side effects such as sending emails, notifications, or publishing integration events.
/// </summary>
/// <remarks><b>Lifetime:</b> Registered as <b>Scoped</b>. A new instance is created per request/scope.</remarks>
/// <typeparam name="TEvent">Type of domain event this handler processes.</typeparam>
public interface IDomainPostCommitHandler<in TEvent>
    : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent;
