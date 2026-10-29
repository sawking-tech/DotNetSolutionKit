using NamespaceRoot.ProductName.Common.Domain.Messaging;

namespace NamespaceRoot.ProductName.Common.Application.Messaging;

/// <summary>
/// Abstraction over the messaging infrastructure for use within application Use Cases.
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publishes an event to all subscribed consumers (fan-out).
    /// Every microservice with a matching consumer receives a copy.
    /// </summary>
    Task PublishAsync<T>(T busEvent, CancellationToken ct = default)
        where T : class, IBusEvent;

    /// <summary>
    /// Sends a command to a specific queue (point-to-point).
    /// Exactly one consumer processes the command.
    /// </summary>
    Task SendAsync<T>(T command, CancellationToken ct = default)
        where T : class, IBusCommand;
}
