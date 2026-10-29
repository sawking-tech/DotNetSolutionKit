using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Captures database command exceptions to be used during the Rollback phase.
/// </summary>
/// <remarks>
/// <b>Note:</b> Must be registered as a <b>Singleton</b> to support <b>DbContextPool</b>.
/// Infrastructure is resolved via <see cref="DomainEventInfrastructureResolver"/>.
/// </remarks>
public sealed class DomainEventErrorInterceptor : DbCommandInterceptor
{
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        CaptureException(eventData);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken ct = default)
    {
        CaptureException(eventData);
        return base.CommandFailedAsync(command, eventData, ct);
    }

    private static void CaptureException(CommandErrorEventData eventData)
    {
        // Guard Clause: Ensure context exists.
        if (eventData.Context is null) return;

        // Filter: Capture exceptions only within an active transaction (save/update operations).
        if (eventData.Context.Database.CurrentTransaction == null) return;

        // Resolve storage through the helper and attach the exception for the Rollback phase.
        if (DomainEventInfrastructureResolver.TryResolve(eventData.Context, out var storage, out _))
        {
            storage.LastException = eventData.Exception;
        }
    }
}
