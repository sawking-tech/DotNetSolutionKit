using NamespaceRoot.ProductName.Common.Application.Messaging;
using NamespaceRoot.ProductName.Common.Domain.Messaging;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

internal sealed class MassTransitMessageBus(
    IPublishEndpoint publishEndpoint,
    ISendEndpointProvider sendEndpointProvider,
    ILogger<MassTransitMessageBus> logger)
    : IMessageBus
{
    public async Task PublishAsync<T>(T busEvent, CancellationToken ct = default)
        where T : class, IBusEvent
    {
        logger.LogDebug(
            "Publishing event {EventType} Id={EventId}",
            typeof(T).Name, busEvent.Id);

        await publishEndpoint.Publish(busEvent, ct);
    }

    public async Task SendAsync<T>(T command, CancellationToken ct = default)
        where T : class, IBusCommand
    {
        logger.LogDebug(
            "Sending command {CommandType} Id={CommandId}",
            typeof(T).Name, command.Id);

        // MassTransit convention-based routing: exchange name = message type short name.
        // Works with RabbitMQ + KebabCaseEndpointNameFormatter.
        await sendEndpointProvider.Send(command, ct);
    }

    public async Task SendAsync<T>(T command, Uri destination, CancellationToken ct = default)
        where T : class, IBusCommand
    {
        logger.LogDebug(
            "Sending command {CommandType} Id={CommandId} to {Destination}",
            typeof(T).Name, command.Id, destination);

        var endpoint = await sendEndpointProvider.GetSendEndpoint(destination);
        await endpoint.Send(command, ct);
    }
}
