// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Finalizes the domain event lifecycle by executing Post-Commit or Rollback phases.
/// </summary>
/// <remarks>
/// <b>Note:</b> Must be registered as a <b>Singleton</b> to support <b>DbContextPool</b>.
/// Infrastructure is resolved via <see cref="DomainEventInfrastructureResolver"/>.
/// </remarks>
public sealed class DomainEventTransactionInterceptor : DbTransactionInterceptor
{
    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken ct = default)
    {
        // PHASE 2: Post-Commit. The snapshot and the IsDispatching guard live in DomainEventCompletion:
        // a PostCommit handler that commits its own transaction on another DbContext in the SAME scope
        // re-enters here and must neither re-dispatch the outer events nor clear them mid-flight.
        if (eventData.Context is { } context)
            await DomainEventCompletion.CommittedAsync(context, ct);

        await base.TransactionCommittedAsync(transaction, eventData, ct);
    }

    public override async Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken ct = default)
    {
        // PHASE 3: Rollback, with the exception the pre-save phase captured.
        if (eventData.Context is { } context)
            await DomainEventCompletion.RolledBackAsync(context, ct);

        await base.TransactionRolledBackAsync(transaction, eventData, ct);
    }
}
