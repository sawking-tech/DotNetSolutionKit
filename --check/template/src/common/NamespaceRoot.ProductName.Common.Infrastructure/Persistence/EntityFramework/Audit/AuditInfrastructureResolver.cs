using NamespaceRoot.ProductName.Common.Application.Messaging;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;

/// <summary>
/// Resolves the scoped services the audit interceptor needs from a singleton interceptor running
/// under <c>DbContextPool</c>. Mirrors <see cref="DomainEventInfrastructureResolver"/>: the scope
/// comes from the HTTP request, or from the ambient scope published by the MassTransit filter and
/// the Hangfire job activator.
/// </summary>
internal static class AuditInfrastructureResolver
{
    public static bool TryResolve(
        DbContext context,
        out IDomainExecutionContext execution,
        out IMessageBus bus)
    {
        execution = null!;
        bus = null!;

        var internalSp = (context as IInfrastructure<IServiceProvider>)?.Instance;
        if (internalSp is null) return false;

        var appSp = internalSp.GetService<IDbContextOptions>()
            ?.Extensions.OfType<CoreOptionsExtension>().FirstOrDefault()
            ?.ApplicationServiceProvider;

        var httpAccessor = appSp?.GetService<IHttpContextAccessor>();

        IServiceProvider?[] candidates =
        [
            httpAccessor?.HttpContext?.RequestServices,
            DomainEventScopeContext.Current,
        ];

        foreach (var candidate in candidates)
        {
            if (candidate is null) continue;
            execution = candidate.GetService<IDomainExecutionContext>()!;
            bus = candidate.GetService<IMessageBus>()!;
            if (execution is not null && bus is not null)
                return true;
        }

        // Seeding, migrations and self-managed processing scopes land here. Changes made there are
        // not attributable to anyone, so dropping them costs the journal nothing — but say it out
        // loud, because a consumer that forgot DomainEventScopeContext.Use looks identical.
        internalSp.GetService<IDiagnosticsLogger<DbLoggerCategory.Infrastructure>>()?.Logger
            .LogDebug(
                "Audit capture skipped for {Context}: no DI scope resolvable. Expected during seeding and " +
                "migrations; unexpected inside a consumer, which must wrap handling in DomainEventScopeContext.Use.",
                context.GetType().Name);

        execution = null!;
        bus = null!;

        return false;
    }
}
