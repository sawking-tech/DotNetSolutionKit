using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Application.Events;

/// <summary>
/// Orchestrates the execution of domain event handlers across different phases
/// of the transaction lifecycle (Pre-Save, Post-Commit, Rollback).
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Executes handlers intended to run before changes are persisted to the database.
    /// </summary>
    /// <param name="events">The collection of domain events to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchPreSaveAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);

    /// <summary>
    /// Executes handlers intended to run after the transaction has been successfully committed.
    /// </summary>
    /// <param name="events">The collection of domain events to dispatch.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchPostCommitAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);

    /// <summary>
    /// Executes handlers intended to run if the transaction is rolled back.
    /// </summary>
    /// <param name="events">The collection of domain events to dispatch.</param>
    /// <param name="exception">
    /// The exception that triggered the rollback, or <c>null</c> if the rollback was initiated manually.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchRollbackAsync(IEnumerable<IDomainEvent> events, Exception? exception, CancellationToken ct = default);
}
