using NamespaceRoot.ProductName.Common.Application.Events.Handlers;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using Microsoft.Extensions.DependencyInjection;

namespace NamespaceRoot.ProductName.Common.Tests;

/// <summary>
/// Registers the domain event pipeline with an explicit handler set instead of scanning a whole
/// assembly. Lets tests isolate the handler under test without dragging in unrelated handlers
/// whose dependencies would otherwise have to be mocked or stubbed to keep DI happy.
/// </summary>
/// <remarks>
/// Pass no handler types to register only the dispatcher - used when the code under test raises
/// no events itself but seeded entities still raise them from their constructors.
/// </remarks>
public static class DomainEventsTestExtensions
{
    /// <summary>
    /// Registers the dispatcher infrastructure and each given handler under every phase interface
    /// it implements (<see cref="IDomainPreSaveHandler{TEvent}"/>, <see cref="IDomainPostCommitHandler{TEvent}"/>, …).
    /// </summary>
    /// <param name="services">DI container to register into.</param>
    /// <param name="handlerTypes">Concrete handler classes. Pass none for dispatcher-only.</param>
    public static IServiceCollection AddDomainEvents(
        this IServiceCollection services,
        params Type[] handlerTypes)
    {
        DomainEventHandlerPhases.ThrowIfOneHandleServesTwoPhases(handlerTypes);
        services.AddDomainEventCore().AddDomainEventPersistence();

        foreach (var handlerType in handlerTypes)
        {
            // Register under every generic interface (IDomainPreSaveHandler<T>,
            // IDomainPostCommitHandler<T>, IDomainEventHandler<T>, …) - mirrors
            // what Scrutor's AsImplementedInterfaces() does in AddDomainEventHandlers.
            foreach (var iface in handlerType.GetInterfaces().Where(i => i.IsGenericType))
                services.AddScoped(iface, handlerType);
        }

        return services;
    }
}
