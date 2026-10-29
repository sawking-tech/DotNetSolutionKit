using NamespaceRoot.ProductName.Common.Domain.Messaging;
using NamespaceRoot.ProductName.Common.Infrastructure.Messaging;
using MassTransit;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Messaging;

[TestFixture]
[TestOf(typeof(PrefixedEventEndpointNameFormatter))]
[Parallelizable(ParallelScope.All)]
public class PrefixedEventEndpointNameFormatterTests
{
    private static readonly PrefixedEventEndpointNameFormatter Formatter = new("orders");

    [Test]
    [Description("Event consumer queues get the per-service prefix so identically named consumers in different services do not share a queue")]
    public void Consumer_EventConsumer_ReturnsPrefixedKebab()
    {
        Formatter.Consumer<SampleOrderPlacedConsumer>().ShouldBe("orders-sample-order-placed");
    }

    [Test]
    [Description("Command consumer queues stay prefix-free so cross-service EndpointConvention.Map lands on the same address regardless of sender")]
    public void Consumer_CommandConsumer_ReturnsKebabWithoutPrefix()
    {
        Formatter.Consumer<SampleSendEmailCommandConsumer>().ShouldBe("sample-send-email-command");
    }

    [Test]
    [Description("Message<TEvent>() prefixes the event type — used when MassTransit derives publish/receive names from the message type")]
    public void Message_BusEvent_ReturnsPrefixed()
    {
        Formatter.Message<SampleOrderPlaced>().ShouldBe("orders-sample-order-placed");
    }

    [Test]
    [Description("Message<TCommand>() returns prefix-free so EndpointConvention.Map<TCommand> targets the shared cross-service address")]
    public void Message_BusCommand_ReturnsUnprefixed()
    {
        Formatter.Message<SampleSendEmailCommand>().ShouldBe("sample-send-email-command");
    }

    private sealed record SampleOrderPlaced(Guid Id, DateTimeOffset OccurredOnUtc) : IBusEvent;
    private sealed record SampleSendEmailCommand(Guid Id, DateTimeOffset OccurredOnUtc) : IBusCommand;

    private sealed class SampleOrderPlacedConsumer : IConsumer<SampleOrderPlaced>
    {
        public Task Consume(ConsumeContext<SampleOrderPlaced> context) => Task.CompletedTask;
    }

    private sealed class SampleSendEmailCommandConsumer : IConsumer<SampleSendEmailCommand>
    {
        public Task Consume(ConsumeContext<SampleSendEmailCommand> context) => Task.CompletedTask;
    }
}
