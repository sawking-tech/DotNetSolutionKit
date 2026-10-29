using NamespaceRoot.ProductName.Common.Application.Messaging;
using NamespaceRoot.ProductName.Common.Domain.Messaging;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

internal sealed class NoOpMessageBus(
    ILogger<NoOpMessageBus> logger) : IMessageBus
{
    public Task PublishAsync<T>(T busEvent, CancellationToken ct = default)
        where T : class, IBusEvent
    {
        logger.LogWarning(
            "Messaging disabled. Event {EventType} was NOT published",
            typeof(T).Name);

        return Task.CompletedTask;
    }

    public Task SendAsync<T>(T command, CancellationToken ct = default)
        where T : class, IBusCommand
    {
        logger.LogWarning(
            "Messaging disabled. Command {CommandType} was NOT sent",
            typeof(T).Name);

        return Task.CompletedTask;
    }
}
