namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Ambient scope holder for domain event infrastructure resolution in non-HTTP contexts.
/// Uses <see cref="AsyncLocal{T}"/> to propagate the current DI scope across async call chains
/// (e.g., MassTransit consumers, Hangfire jobs).
/// </summary>
/// <remarks>
/// MassTransit consumers get this for free via <see cref="DomainEventScopeFilter{T}"/>. Any
/// OTHER self-managed processing scope (raw RabbitMQ consumers, custom background loops) must
/// wrap its handler invocation in <see cref="Use"/> - otherwise the domain-event interceptors
/// silently skip the scope's events (no storage resolvable) and PostCommit handlers never fire.
/// </remarks>
public static class DomainEventScopeContext
{
    private static readonly AsyncLocal<IServiceProvider?> _current = new();

    /// <summary>Gets the current ambient scope's service provider, or null if not set.</summary>
    public static IServiceProvider? Current => _current.Value;

    /// <summary>
    /// Sets the ambient scope to the given <paramref name="serviceProvider"/> for the duration of the returned scope.
    /// Restores the previous value on dispose.
    /// </summary>
    public static IDisposable Use(IServiceProvider serviceProvider)
    {
        var previous = _current.Value;
        _current.Value = serviceProvider;
        return new RestoreScope(() => _current.Value = previous);
    }

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
