using NamespaceRoot.ProductName.Common.Application.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;

/// <summary>
/// Registration of the audit capture pipeline.
/// </summary>
public static class AuditPersistenceExtensions
{
    /// <summary>
    /// Registers the audit interceptor.
    /// </summary>
    /// <remarks>
    /// WARNING: Singleton to support DbContextPool. The constructor stays empty; scoped services are
    /// resolved per save through <see cref="AuditInfrastructureResolver"/>.
    /// </remarks>
    public static IServiceCollection AddAuditPersistence(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<AuditSaveChangesInterceptor>();

        return services;
    }

    /// <summary>
    /// Registers the explicit recorder for changes the interceptor cannot observe, filing them under
    /// <paramref name="sourceService"/>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="AddAuditPersistence"/> because it is needed only by services that
    /// write set-based, such as a balance moved with <c>ExecuteUpdateAsync</c>. See <see cref="IAuditRecorder"/> for
    /// when reaching for it is right and when it is not.
    /// </remarks>
    public static IServiceCollection AddAuditRecorder<TDbContext>(
        this IServiceCollection services, string sourceService)
        where TDbContext : DbContextBase
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceService);

        services.AddSingleton<ISourceServiceName>(new SourceServiceName(sourceService));
        services.AddScoped<IAuditRecorder, AuditRecorder>();

        // The capture reads through the service's own context; DI holds it under its concrete type,
        // so the base type is aliased here rather than asking every caller to register it.
        services.AddScoped<DbContextBase>(sp => sp.GetRequiredService<TDbContext>());
        services.AddScoped<ISetBasedAuditCapture, SetBasedAuditCapture>();

        return services;
    }

    /// <summary>
    /// Attaches the audit interceptor to a context.
    /// Use inside AddDbContext((sp, options) =&gt; { options.ApplyAuditInterceptor(sp); }).
    /// </summary>
    public static void ApplyAuditInterceptor(this DbContextOptionsBuilder options, IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);

        options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
    }
}
