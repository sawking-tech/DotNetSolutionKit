using System.Diagnostics.CodeAnalysis;
using NamespaceRoot.ProductName.Common.Application.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Provides a unified way to resolve scoped domain event infrastructure
/// from singleton interceptors within DbContextPool.
/// </summary>
internal static class DomainEventInfrastructureResolver
{
    [SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract")]
    [SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract")]
    public static bool TryResolve(
        DbContext context,
        out IDomainEventStorage storage,
        out IDomainEventDispatcher dispatcher,
        bool eventsPending = false)
    {
        storage = null!;
        dispatcher = null!;

        // 1. Get the internal EF service provider safely.
        var internalSp = (context as IInfrastructure<IServiceProvider>)?.Instance;
        if (internalSp is null) return false;

        // 2. Get the EF Diagnostics Logger to provide visibility for DI issues (addressing review comments).
        var diagLogger = internalSp.GetService<IDiagnosticsLogger<DbLoggerCategory.Infrastructure>>();

        // 3. Identify the Root Application Provider.
        var appSp = internalSp.GetService<IDbContextOptions>()
            ?.Extensions.OfType<CoreOptionsExtension>().FirstOrDefault()
            ?.ApplicationServiceProvider;

        // 4. Determine the best available scope.
        // HTTP requests: IHttpContextAccessor.HttpContext.RequestServices
        // MassTransit:   DomainEventScopeContext set by DomainEventScopeFilter
        // Hangfire:      DomainEventScopeContext set by DomainEventJobActivator
        // Seeding/Migrations: no scope → returns false, events are silently skipped.
        var httpAccessor = appSp?.GetService<IHttpContextAccessor>();

        // 5. Try providers in priority order until both services are found.
        IServiceProvider?[] candidates =
        [
            httpAccessor?.HttpContext?.RequestServices,
            DomainEventScopeContext.Current,
        ];

        foreach (var candidate in candidates)
        {
            if (candidate is null) continue;
            storage = candidate.GetService<IDomainEventStorage>()!;
            dispatcher = candidate.GetService<IDomainEventDispatcher>()!;
            if (storage is not null && dispatcher is not null)
                return true;
        }

        if (eventsPending)
        {
            // The caller has harvested actual domain events and this failure means they are about
            // to be silently discarded - a self-managed processing scope (raw broker consumer,
            // custom background loop) forgot DomainEventScopeContext.Use. Loud on purpose: dropped
            // events raise no error anywhere else, and the handlers that should have run never do.
            diagLogger?.Logger.LogWarning(
                "Domain events are pending on {Context} but no DI scope is resolvable - the events will be DROPPED. " +
                "Non-MassTransit consumers must wrap handling in DomainEventScopeContext.Use(scope.ServiceProvider).",
                context.GetType().Name);
        }
        else
        {
            diagLogger?.Logger.LogDebug(
                "DomainEvent resolution skipped: no active scope found for {Context}. Expected where nothing was raised - " +
                "outbox delivery, seeding, migrations. Hangfire jobs DO get a scope via DomainEventJobActivator.",
                context.GetType().Name);
        }

        storage = null!;
        dispatcher = null!;

        return false;
    }
}
