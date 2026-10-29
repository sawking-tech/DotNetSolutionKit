# Messaging (MassTransit + RabbitMQ)

`Common.Infrastructure.Messaging` wires MassTransit for every service through one call, `AddMessaging`;
a service installs no MassTransit package of its own. The examples below use an `orders` service.

## Registration

### With the transactional outbox

```csharp
services.AddMessaging<OrdersDbContext>(configuration, "orders", typeof(Program).Assembly);
```

A message is written to the service's database in the same transaction as the business data, and a
background relay delivers it to RabbitMQ after the commit. If the broker is down, the message waits
in the outbox table.

### Without the outbox

```csharp
services.AddMessaging(configuration, "orders", typeof(Program).Assembly);
```

A message goes straight to RabbitMQ. If the broker is down, the message is lost. Use it for a
service without its own database, or for messages nobody misses.

The second argument is the service's queue prefix, in kebab-case. An event consumer's queue is named
`<prefix>-<event-name>`, so two services with identically named `IConsumer<TEvent>` classes get two
queues; with one shared queue they would split the events between them. A command consumer's queue
has no prefix, so `EndpointConvention.Map<TCommand>` points at the same address whichever service
hosts the consumer.

### Switched off

```json
{
  "RabbitMq": {
    "Enabled": false
  }
}
```

`Enabled` is false by default. `NoOpMessageBus` is then registered: publishing and sending do
nothing, and nothing connects to a broker.

## DbContext setup (outbox only)

Add the outbox tables to the `DbContext`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.AddTransactionalOutbox("orders");
}
```

Then add a migration:

```bash
dotnet ef migrations add AddOutbox
dotnet ef database update
```

## Message contracts

Contracts live in `Common.Contracts`. Each message implements `IBusCommand` or `IBusEvent`:

```csharp
public record SendOrderConfirmationCommandV1(
    Guid OrderId,
    string Email) : IBusCommand
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

| Type | Delivery | Consumers |
|------|----------|-----------|
| `IBusCommand` | Point-to-point | One; a second consumer of the same command fails at startup |
| `IBusEvent` | Fan-out | Any number |

## Publishing

### Events

```csharp
await messageBus.PublishAsync(new OrderPlacedV1(...), ct);
```

### Commands

```csharp
// The address comes from the command type
await messageBus.SendAsync(new SendOrderConfirmationCommandV1(...), ct);

// An explicit address
await messageBus.SendAsync(new SendOrderConfirmationCommandV1(...), new Uri("queue:notifications"), ct);
```

### From a domain event handler (pre-save)

```csharp
public sealed class OrderConfirmationHandler(IMessageBus messageBus)
    : IDomainPreSaveHandler<OrderPlacedEvent>
{
    public async Task Handle(OrderPlacedEvent @event, CancellationToken ct, object? data)
    {
        await messageBus.SendAsync(new SendOrderConfirmationCommandV1(...), ct);
    }
}
```

## Outbox and transactions

The outbox catches a message only when `PublishAsync()` or `SendAsync()` is called inside an open
transaction, before `SaveChanges()`.

| Where the message is sent | Goes through the outbox |
|---------------------------|-------------------------|
| Pre-save handler, before `SaveChanges` | Yes |
| Post-commit handler, after `SaveChanges` | No |
| Rollback handler | No |
| Outside any transaction | No |

To send through the outbox where no business transaction is open, as in a background job or a
post-commit handler, open one:

```csharp
await using var context = await dbContextFactory.CreateDbContextAsync(ct);
await using var tx = await context.Database.BeginTransactionAsync(ct);

await messageBus.SendAsync(new SendOrderConfirmationCommandV1(...), ct);
await context.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

The outbox record is written in that transaction, and the relay delivers it after the commit. Prefer
the pre-save handler where you can: there the message and the business data commit together.

## Consuming

`AddMessaging` registers the consumers it finds in the assemblies passed to it. Inherit
`BusCommandConsumer<T>` for a command and `BusEventConsumer<T>` for an event.

### Command consumers are named `{CommandTypeName}Consumer`

```csharp
// Command contract in Common.Contracts:
public record SendOrderConfirmationCommandV1 : IBusCommand { ... }

// Consumer in the service's infrastructure:
public sealed class SendOrderConfirmationCommandV1Consumer(IEmailSender emailSender)
    : BusCommandConsumer<SendOrderConfirmationCommandV1>
{ ... }
```

`EndpointConvention` sends a command to a queue named after the command type in kebab-case
(`send-order-confirmation-command-v1`). `ConfigureEndpoints` names the receive endpoint after the
consumer class without the `Consumer` suffix, also in kebab-case. The two names match only when the
consumer is called `{CommandTypeName}Consumer`; a startup check throws when one is not.

### Event consumers

Any name works: an event is delivered to every subscriber, with no point-to-point address to match.

```csharp
public sealed class OrderPlacedConsumer(IRepository repo)
    : BusEventConsumer<OrderPlacedV1>
{ ... }
```

## Configuration

```json
{
  "RabbitMq": {
    "Enabled": true,
    "Host": "localhost",
    "UserName": "guest",
    "Password": "guest",
    "PurgeOnStartup": false,
    "RetryLimit": 3,
    "RetryInitialIntervalSeconds": 1,
    "RetryIntervalIncrementSeconds": 2
  }
}
```

The values shown for `Host`, the credentials and the retries are the defaults.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
The current version of the message bus: [dotskit.sawking.tech](https://dotskit.sawking.tech/docs.html#messaging).
