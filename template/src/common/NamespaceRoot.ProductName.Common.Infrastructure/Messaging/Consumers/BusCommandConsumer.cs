using NamespaceRoot.ProductName.Common.Application.Messaging.Consumers;
using NamespaceRoot.ProductName.Common.Domain.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging.Consumers;

/// <summary>
/// Base class for command consumers. Provides structured logging,
/// error handling, and IMessageContext abstraction.
/// Inherit in microservice infrastructure layer.
/// </summary>
public abstract class BusCommandConsumer<T>(
    ILogger logger)
    : IConsumer<T>
    where T : class, IBusCommand
{
    public async Task Consume(ConsumeContext<T> consumeContext)
    {
        var commandName = typeof(T).Name;
        var messageId = consumeContext.MessageId;

        logger.LogDebug(
            "Consuming command {CommandType} MessageId={MessageId}",
            commandName, messageId);

        try
        {
            var context = new MassTransitMessageContext<T>(consumeContext);
            await HandleAsync(context);

            logger.LogInformation(
                "Consumed command {CommandType} MessageId={MessageId}",
                commandName, messageId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to consume command {CommandType} MessageId={MessageId}",
                commandName, messageId);
            throw;
        }
    }

    /// <summary>
    /// Implement command handling logic here.
    /// </summary>
    protected abstract Task HandleAsync(IMessageContext<T> context);
}
