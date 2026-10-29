# Message bus

Off by default. `--Messaging` chooses per service:

| Value | What the service gets |
|---|---|
| `none` | no bus |
| `outbox` | MassTransit on RabbitMQ, with a transactional outbox in the service's schema: a message is stored with the change that caused it and sent after the commit |
| `direct` | MassTransit on RabbitMQ without an outbox: a message goes straight to the broker and is lost if the broker is down |

Choose `outbox` when a message must not be lost and must not be sent for a change that rolled back;
`direct` only where losing a message is acceptable.

## Publish

From a pre-save domain event handler, the message goes into the outbox inside the transaction of the
write that raised the event:

```csharp
public sealed class PublishOrderPlaced(IMessageBus bus) : IDomainPreSaveHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced e, CancellationToken ct, object? data = null) =>
        bus.PublishAsync(new OrderPlacedV1 { OrderId = e.OrderId }, ct);
}
```

An event implements `IBusEvent`, a command `IBusCommand`. Contracts shared between services live in
`Common.Contracts`, versioned in the name (`OrderPlacedV1`), so a breaking change is a new type next to the
old one.

## Consume

Consumers live in the service's infrastructure and are found by scanning. Queues are named after the
service (`orders`, `sales_orders`), so two services consuming one event each get their copy.

## What travels with a message

The user who caused the message, the trace (`traceparent`) and the
[correlation identifier](../architecture/web-layer.md#correlation) are written into its headers and
restored for the consumer, so what a consumer writes is attributed to that user and its log lines are
found by the identifier of the request that caused them. The outbox keeps the headers: a message
delivered from it later still carries them.
Domain events raised in a consumer are dispatched as in a request.

## Configuration

```json
"RabbitMq": { "Enabled": true }
```

Host, credentials and retries have defaults (`localhost`, `guest`, 3 retries); pass real credentials as
`RabbitMq__UserName` and `RabbitMq__Password`. `Enabled: false` registers a bus that does nothing and logs
a critical line at startup. With a bus, `/ready` checks the broker.

With the outbox, `GET /api/v1/diagnostics/outbox-stats` shows how many messages wait and were sent, and the
latest ones; `?filter=` matches a message body, an order id for instance. The endpoint exists outside
Production only.

Registration, contracts, publishing, the outbox and consumers in detail:
[Messaging/README.md](../../template/src/common/NamespaceRoot.ProductName.Common.Infrastructure/Messaging/README.md).

## Tests

`Common.Tests` covers the endpoint naming, command addresses, and the propagation of the actor and the
trace. A consumer is tested like a service, see [ADR-005](../adr/005-testing-a-service.md).
