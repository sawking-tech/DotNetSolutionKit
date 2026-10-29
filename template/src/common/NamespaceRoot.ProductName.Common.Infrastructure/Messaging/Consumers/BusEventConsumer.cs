using NamespaceRoot.ProductName.Common.Application.Messaging.Consumers;
using NamespaceRoot.ProductName.Common.Domain.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging.Consumers;

/// <summary>
/// Base class for event consumers. Provides structured logging,
/// error handling, and IMessageContext abstraction.
/// Inherit in microservice infrastructure layer.
/// </summary>
public abstract class BusEventConsumer<T>(
    ILogger logger)
    : IConsumer<T>
    where T : class, IBusEvent
{
    public async Task Consume(ConsumeContext<T> consumeContext)
    {
        var eventName = typeof(T).Name;
        var messageId = consumeContext.MessageId;

        logger.LogDebug(
            "Consuming event {EventType} MessageId={MessageId}",
            eventName, messageId);

        try
        {
            var context = new MassTransitMessageContext<T>(consumeContext);
            await HandleAsync(context);

            logger.LogInformation(
                "Consumed event {EventType} MessageId={MessageId}",
                eventName, messageId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to consume event {EventType} MessageId={MessageId}",
                eventName, messageId);
            throw;
        }
    }

    /// <summary>
    /// Implement message handling logic here.
    /// </summary>
    protected abstract Task HandleAsync(IMessageContext<T> context);
}
