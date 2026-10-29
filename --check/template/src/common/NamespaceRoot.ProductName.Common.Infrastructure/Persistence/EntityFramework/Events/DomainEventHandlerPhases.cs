using NamespaceRoot.ProductName.Common.Application.Events.Handlers;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Refuses, at registration, a handler that would run in two phases through one method.
/// </summary>
/// <remarks>
/// <see cref="IDomainPreSaveHandler{TEvent}"/> and <see cref="IDomainPostCommitHandler{TEvent}"/> both take
/// their <c>Handle</c> from <see cref="IDomainEventHandler{TEvent}"/>, so a class implementing both for one
/// event is registered under both and called before the save and again after the commit, with the same
/// method and no way to tell the phases apart. A rollback handler has a method of its own,
/// <c>HandleRollback</c>, and goes with either.
/// </remarks>
public static class DomainEventHandlerPhases
{
    public static void ThrowIfOneHandleServesTwoPhases(IEnumerable<Type> handlerTypes)
    {
        foreach (var type in handlerTypes)
        {
            var preSave = EventsOf(type, typeof(IDomainPreSaveHandler<>));
            var shared = EventsOf(type, typeof(IDomainPostCommitHandler<>)).Where(preSave.Contains).ToList();
            if (shared.Count > 0)
                throw new InvalidOperationException(
                    $"{type.FullName} handles {string.Join(", ", shared.Select(e => e.Name))} both before the save and " +
                    "after the commit, through one Handle method that would run in both phases. " +
                    "Make it two classes, one per phase.");
        }
    }

    /// <summary>The types of <paramref name="assembly"/> that load, as the scan of the handlers sees them.</summary>
    public static IEnumerable<Type> LoadableTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static HashSet<Type> EventsOf(Type handler, Type openPhase) =>
        handler.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openPhase)
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();
}
