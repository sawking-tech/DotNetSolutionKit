// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using NamespaceRoot.ProductName.Common.Application.Events.Handlers;
using NamespaceRoot.ProductName.Common.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Application.Events;

/// <summary>
/// Orchestrates the execution of domain event handlers across different transaction phases.
/// Resolves handlers from the DI container and ensures proper data passing.
/// </summary>
/// <remarks>
/// Execution scopes differ by phase - this is the core of the contract:
/// <list type="bullet">
///   <item><b>PreSave</b> handlers run in the AMBIENT scope: same DbContext, same open
///   transaction. That is the point - their writes commit or roll back atomically with the
///   triggering change, and a bus publish lands in the same outbox window.</item>
///   <item><b>PostCommit / Rollback</b> handlers run in a FRESH DI scope (one per event).
///   These phases fire from inside the ambient transaction's <c>CommitAsync</c> /
///   <c>RollbackAsync</c>, where the ambient UnitOfWork still holds the completing transaction -
///   a handler using ambient scoped services would hit «A transaction is already in progress»
///   and its bus publishes would miss the closed outbox window. The fresh scope gives every
///   handler its own DbContext, transaction capability and outbox, making DB writes and bus
///   publishing safe by construction (wrap them in the handler's own Begin/Save/Commit).</item>
/// </list>
/// </remarks>
public sealed class DomainEventDispatcher(
    IServiceProvider serviceProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    /// <inheritdoc />
    public Task DispatchPreSaveAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        => DispatchAsync(typeof(IDomainPreSaveHandler<>), events, ct);

    /// <inheritdoc />
    public Task DispatchPostCommitAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        => DispatchAsync(typeof(IDomainPostCommitHandler<>), events, ct);

    /// <inheritdoc />
    public Task DispatchRollbackAsync(IEnumerable<IDomainEvent> events, Exception? exception, CancellationToken ct = default)
        => DispatchAsync(typeof(IDomainRollbackHandler<>), events, ct, exception);

    // A class with one Handle for two phases of one event is refused at registration
    // (DomainEventHandlerPhases), so a handler resolved here belongs to this phase only.

    /// <summary>
    /// Dispatches events to handlers matching the specific phase interface and event type.
    /// </summary>
    private async Task DispatchAsync(Type openHandlerType, IEnumerable<IDomainEvent> events, CancellationToken ct, object? data = null)
    {
        // PreSave runs in the ambient scope on purpose - see the class remarks.
        var isolatePhase = openHandlerType != typeof(IDomainPreSaveHandler<>);

        foreach (var @event in events)
        {
            // Resolve concrete phase interface (e.g., IDomainPostCommitHandler<UserCreatedEvent>)
            var handlerType = openHandlerType.MakeGenericType(@event.GetType());

            if (!isolatePhase)
            {
                await InvokeHandlersAsync(serviceProvider, handlerType, openHandlerType, @event, ct, data);
                continue;
            }

            // One fresh scope per event: handlers get their own DbContext/outbox, isolated from
            // the ambient completing transaction AND from each other's previous events.
            await using var scope = scopeFactory.CreateAsyncScope();
            await InvokeHandlersAsync(scope.ServiceProvider, handlerType, openHandlerType, @event, ct, data);
        }
    }

    private async Task InvokeHandlersAsync(
        IServiceProvider provider, Type handlerType, Type openHandlerType, IDomainEvent @event,
        CancellationToken ct, object? data)
    {
        foreach (var handler in provider.GetServices(handlerType))
        {
            // Bridge call via the non-generic IDomainEventHandler interface
            if (handler is IDomainEventHandler baseHandler)
            {
                try
                {
                    await baseHandler.Handle(@event, ct, data);
                }
                catch (Exception ex)
                {
                    // PreSave: re-throw so the transaction rolls back.
                    // PostCommit / Rollback: log and swallow - the transaction is already
                    // committed (or rolling back) and an exception here cannot undo it. Silent
                    // swallowing without a log entry hides bus-publish failures and similar
                    // side-effect bugs; ERROR-level log ensures they surface in monitoring.
                    if (openHandlerType == typeof(IDomainPreSaveHandler<>)) throw;

                    logger.LogError(ex,
                        "Domain event handler {HandlerType} failed during {Phase} for event {EventType}",
                        handler.GetType().Name,
                        openHandlerType == typeof(IDomainPostCommitHandler<>) ? "PostCommit" : "Rollback",
                        @event.GetType().Name);
                }
            }
        }
    }
}
