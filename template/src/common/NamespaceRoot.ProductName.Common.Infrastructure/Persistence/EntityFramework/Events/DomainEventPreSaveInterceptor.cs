using NamespaceRoot.ProductName.Common.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;

using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Interceptor that captures domain events and executes the Pre-Save phase.
/// </summary>
/// <remarks>
/// <b>Note:</b> Must be registered as a <b>Singleton</b> to support <b>DbContextPool</b>.
/// Infrastructure is resolved via <see cref="DomainEventInfrastructureResolver"/>.
/// </remarks>
public sealed class DomainEventPreSaveInterceptor : SaveChangesInterceptor
{
    // Safety cap against a runaway PreSave handler that keeps emitting new events on every pass.
    // The loop's terminating invariant is "no entities with pending events left" - this cap is
    // only here to fail loudly instead of looping forever when a handler is buggy. Real domain
    // cascades are shallow (depth 2-3); anything above this is almost certainly a bug.
    private const int RunawayDetectionLimit = 20;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var context = data.Context;

        // Guard: Skip if no context or no pending changes.
        if (context is null || !context.ChangeTracker.HasChanges())
            return await base.SavingChangesAsync(data, result, ct);

        // Identify entities implementing IHasDomainEvents that have pending events.
        // We use the interface to support explicit implementation and decoupling.
        var entries = context.ChangeTracker.Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Any())
            .ToList();

        if (entries.Count == 0)
            return await base.SavingChangesAsync(data, result, ct);

        // Resolve infrastructure only when there is actual work to do. eventsPending: entities
        // DO carry events here, so an unresolvable scope means they get dropped - warn loudly.
        if (!DomainEventInfrastructureResolver.TryResolve(context, out var storage, out var dispatcher, eventsPending: true) || storage.IsDispatching)
            return await base.SavingChangesAsync(data, result, ct);

        try
        {
            storage.IsDispatching = true;

            // Fixed-point loop: a PreSave handler may add new aggregates whose constructors raise
            // their own domain events (cascade). The initial ChangeTracker snapshot taken above is
            // already stale by then. Re-scan after each dispatch until no new events surface, so
            // every event reaches IDomainEventStorage and the matching PostCommit handler fires.
            for (var iteration = 0; ; iteration++)
            {
                if (iteration >= RunawayDetectionLimit)
                    throw new InvalidOperationException(
                        $"Domain event cascade did not converge after {RunawayDetectionLimit} iterations - " +
                        "a PreSave handler is producing new events on every pass.");

                if (entries.Count == 0) break;

                // Extract events before clearing them from entities.
                var events = entries.SelectMany(e => e.Entity.DomainEvents).ToList();

                // 1. Store events in scoped storage FIRST.
                // This ensures they are available for TransactionRolledBackAsync if Pre-Save fails.
                storage.AddEvents(events);

                // 2. Clear events from entities to prevent duplicate dispatching on the next pass.
                // Works via explicit interface implementation to maintain domain encapsulation.
                entries.ForEach(e => e.Entity.ClearDomainEvents());

                // 3. PHASE 1: Pre-Save execution (validations, state adjustments within the transaction).
                await dispatcher.DispatchPreSaveAsync(events, ct);

                // 4. Re-scan: PreSave handlers may have attached new entities with new events.
                entries = context.ChangeTracker.Entries<IHasDomainEvents>()
                    .Where(e => e.Entity.DomainEvents.Any())
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            // Capture exception for the Rollback phase to provide diagnostic context.
            storage.LastException = ex;
            throw;
        }
        finally
        {
            storage.IsDispatching = false;
        }

        return await base.SavingChangesAsync(data, result, ct);
    }

    /// <summary>
    /// A save with no transaction open around it is final when it returns: EF Core sends a single statement
    /// without a transaction, so no transaction event would ever start the post-commit phase.
    /// </summary>
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        if (eventData.Context is { } context && DomainEventCompletion.SavesOnItsOwn(context))
            await DomainEventCompletion.CommittedAsync(context, ct);

        return await base.SavedChangesAsync(eventData, result, ct);
    }

    /// <summary>
    /// A failed save with no transaction open around it is final too: nothing was written.
    /// </summary>
    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken ct = default)
    {
        if (eventData.Context is { } context && DomainEventCompletion.SavesOnItsOwn(context))
            await DomainEventCompletion.RolledBackAsync(context, ct);

        await base.SaveChangesFailedAsync(eventData, ct);
    }
}
