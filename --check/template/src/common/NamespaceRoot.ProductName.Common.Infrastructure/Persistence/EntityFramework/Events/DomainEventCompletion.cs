using Microsoft.EntityFrameworkCore;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Runs the post-commit or the rollback phase for the events a save stored, once the write is final.
/// </summary>
/// <remarks>
/// <para>
/// A write becomes final in three ways, and each reaches here. A relational transaction commits or rolls
/// back, which <see cref="DomainEventTransactionInterceptor"/> sees. A save with no transaction open around
/// it commits on its own: EF Core sends a single statement without a transaction at all, so no transaction
/// event fires, and <see cref="DomainEventPreSaveInterceptor"/> finishes it when the save returns or fails.
/// A provider without relational transactions, as the in-memory one of the service tests, commits and
/// rolls back through <see cref="DbContextBase"/> with no transaction event either.
/// </para>
/// <para>
/// The stored events are dispatched once: the first of these to arrive clears the storage, so a save
/// whose own transaction was already seen by the interceptor finds nothing left to dispatch.
/// </para>
/// </remarks>
internal static class DomainEventCompletion
{
    /// <summary>
    /// Whether a save on <paramref name="context"/> commits by itself, with no transaction open around it.
    /// </summary>
    public static bool SavesOnItsOwn(DbContext context) =>
        context.Database.CurrentTransaction is null
        && context is not DbContextBase { HasActiveTransaction: true }
        && System.Transactions.Transaction.Current is null;

    public static async Task CommittedAsync(DbContext context, CancellationToken ct)
    {
        if (!DomainEventInfrastructureResolver.TryResolve(context, out var storage, out var dispatcher))
            return;

        var events = storage.GetEvents().ToList();
        if (events.Count == 0 || storage.IsDispatching)
            return;

        storage.IsDispatching = true;
        try
        {
            await dispatcher.DispatchPostCommitAsync(events, ct);
        }
        finally
        {
            storage.IsDispatching = false;
            storage.Clear();
        }
    }

    public static async Task RolledBackAsync(DbContext context, CancellationToken ct)
    {
        if (!DomainEventInfrastructureResolver.TryResolve(context, out var storage, out var dispatcher))
            return;

        var events = storage.GetEvents().ToList();
        if (events.Count == 0 || storage.IsDispatching)
            return;

        storage.IsDispatching = true;
        try
        {
            await dispatcher.DispatchRollbackAsync(events, storage.LastException, ct);
        }
        finally
        {
            storage.IsDispatching = false;
            storage.Clear();
        }
    }
}
