using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Application.Events;

/// <summary>
/// Scoped storage for domain events that are awaiting transaction completion.
/// This replaces the unreliable 'DbContext.Items' with a typed collection.
/// </summary>
public interface IDomainEventStorage
{
    IReadOnlyCollection<IDomainEvent> GetEvents();
    void AddEvents(IEnumerable<IDomainEvent> events);

    Exception? LastException { get; set; }
    bool IsDispatching { get; set; }

    void Clear();
}

public sealed class DomainEventStorage : IDomainEventStorage
{
    private readonly List<IDomainEvent> _events = new();

    public IReadOnlyCollection<IDomainEvent> GetEvents() => _events.AsReadOnly();

    public void AddEvents(IEnumerable<IDomainEvent> events)
    {
        _events.AddRange(events);
    }

    public Exception? LastException { get; set; }
    public bool IsDispatching { get; set; }

    public void Clear()
    {
        _events.Clear();
        LastException = null;
        IsDispatching = false;
    }
}
