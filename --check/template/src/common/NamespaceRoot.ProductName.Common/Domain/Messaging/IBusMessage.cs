namespace NamespaceRoot.ProductName.Common.Domain.Messaging;

/// <summary>
/// Marker for events — fan-out to all subscribers.
/// Multiple services can listen to the same event.
/// </summary>
public interface IBusEvent : IBusMessage;

/// <summary>
/// Marker for commands — point-to-point to a single consumer.
/// Exactly one service processes the command.
/// </summary>
public interface IBusCommand : IBusMessage;

/// <summary>
/// Base marker for all messages distributed via the message bus.
/// </summary>
public interface IBusMessage
{
    /// <summary>
    /// Unique identifier for the specific message instance.
    /// </summary>
    Guid Id { get; init; }

    /// <summary>
    /// The UTC timestamp when the message was created.
    /// </summary>
    DateTimeOffset OccurredOnUtc { get; init; }
}
