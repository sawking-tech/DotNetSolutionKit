using System.Reflection;
using NamespaceRoot.ProductName.Common.Application.Events;
using NamespaceRoot.ProductName.Common.Application.Events.Handlers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Extension methods for configuring domain event pipeline within Entity Framework Core.
/// </summary>
public static class EntityFrameworkEventExtensions
{
    /// <summary>
    /// Registers the core domain event infrastructure including storage and dispatcher.
    /// </summary>
    public static IServiceCollection AddDomainEventCore(this IServiceCollection services)
    {
        // The dispatcher depends on ILogger to surface PostCommit/Rollback handler failures.
        // AddLogging is idempotent, so callers that already configured logging see no change.
        services.AddLogging();
        services.AddScoped<IDomainEventStorage, DomainEventStorage>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        return services;
    }

    /// <summary>
    /// Automatically discovers and registers all domain event handlers from the specified assemblies.
    /// </summary>
    public static IServiceCollection AddDomainEventHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        DomainEventHandlerPhases.ThrowIfOneHandleServesTwoPhases(assemblies
            .SelectMany(DomainEventHandlerPhases.LoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false }));

        services.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)))
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        return services;
    }

    /// <summary>
    /// Registers Entity Framework Core interceptors.
    /// </summary>
    public static IServiceCollection AddDomainEventPersistence(this IServiceCollection services)
    {
        // WARNING: Must be Singleton to support DbContextPool.
        // Constructors must be empty. Dependencies are resolved via eventData.Context.GetService<T>().
        services.AddHttpContextAccessor();
        services.AddSingleton<DomainEventPreSaveInterceptor>();
        services.AddSingleton<DomainEventTransactionInterceptor>();
        services.AddSingleton<DomainEventErrorInterceptor>();

        return services;
    }

    /// <summary>
    /// Makes Hangfire jobs publish their DI scope as the ambient domain-event scope, so events raised
    /// by a background job reach the dispatcher instead of being dropped.
    /// </summary>
    /// <remarks>
    /// Registration order against <c>AddHangfire</c> does not matter: Hangfire registers its own
    /// activator with <c>TryAddSingleton</c> (so it yields to an earlier registration), and a later
    /// one wins resolution outright.
    /// </remarks>
    public static IServiceCollection AddDomainEventJobActivator(this IServiceCollection services)
    {
        services.AddSingleton<JobActivator>(sp =>
            new DomainEventJobActivator(sp.GetRequiredService<IServiceScopeFactory>()));

        // Carries the operator who triggered a job on demand into the job itself, so work started
        // from an internal endpoint is attributed to the person who started it rather than to the
        // platform. Scheduled runs carry nothing and stay system-attributed.
        services.AddSingleton<JobActorPropagationFilter>();

        return services;
    }

    /// <summary>
    /// Comprehensive registration of the domain event system.
    /// </summary>
    public static IServiceCollection AddDomainEvents(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        return services
            .AddDomainEventCore()
            .AddDomainEventHandlers(assemblies)
            .AddDomainEventPersistence()
            .AddDomainEventJobActivator();
    }

    /// <summary>
    /// Extension for DbContextOptionsBuilder to fluently add all domain event interceptors from DI.
    /// Use this inside AddDbContext((sp, options) => { options.ApplyDomainEventInterceptors(sp); });
    /// </summary>
    public static void ApplyDomainEventInterceptors(this DbContextOptionsBuilder options, IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);

        options.AddInterceptors(
            sp.GetRequiredService<DomainEventPreSaveInterceptor>(),
            sp.GetRequiredService<DomainEventTransactionInterceptor>(),
            sp.GetRequiredService<DomainEventErrorInterceptor>());
    }
}
