using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Application.Events.Handlers;

/// <summary>
/// Handler executed within the database transaction before it is closed.
/// Use for validations or data consistency checks.
/// </summary>
/// <remarks><b>Lifetime:</b> Registered as <b>Scoped</b>. A new instance is created per request/scope.</remarks>
/// <typeparam name="TEvent">Type of domain event this handler processes.</typeparam>
public interface IDomainPreSaveHandler<in TEvent>
    : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent;
