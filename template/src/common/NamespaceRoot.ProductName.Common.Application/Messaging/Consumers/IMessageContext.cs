using NamespaceRoot.ProductName.Common.Domain.Messaging;

namespace NamespaceRoot.ProductName.Common.Application.Messaging.Consumers;

/// <summary>
/// Provides access to message metadata and payload within a consumer handler.
/// Abstracts the underlying transport context from application-level consumers.
/// </summary>
/// <typeparam name="T">The message type being consumed.</typeparam>
public interface IMessageContext<out T> where T : class, IBusMessage
{
    /// <summary>
    /// The deserialized message payload.
    /// </summary>
    T Message { get; }

    /// <summary>
    /// Transport-level unique identifier for this message delivery. Used for idempotency checks.
    /// </summary>
    Guid? MessageId { get; }

    /// <summary>
    /// Correlation identifier for tracking related messages across services in distributed tracing.
    /// </summary>
    Guid? CorrelationId { get; }

    /// <summary>
    /// Cancellation token
    /// </summary>
    CancellationToken CancellationToken { get; }
}
