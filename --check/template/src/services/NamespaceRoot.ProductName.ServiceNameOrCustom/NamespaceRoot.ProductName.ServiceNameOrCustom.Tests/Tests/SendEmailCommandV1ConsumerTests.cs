using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NamespaceRoot.ProductName.Capabilities.Notifications;
using NamespaceRoot.ProductName.Common.Contracts.Messaging.Notifications;
//#if (Storage)
using NamespaceRoot.ProductName.Common.Infrastructure.Storage;
//#endif
using NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.Messaging.Consumers;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Tests.Tests;

/// <summary>
/// The email another service asks for by a bus command goes out through this service's port as it came.
/// </summary>
public sealed class SendEmailCommandV1ConsumerTests
{
    private readonly Mock<INotificationEmailSender> _email = new();
//#if (Storage)
    private readonly Mock<IS3ObjectStorage> _storage = new();
//#endif

    private SendEmailCommandV1Consumer Consumer() => new(
        _email.Object,
//#if (Storage)
        _storage.Object,
//#endif
        NullLogger<SendEmailCommandV1Consumer>.Instance);

    private static ConsumeContext<SendEmailCommandV1> Context(SendEmailCommandV1 command) =>
        Mock.Of<ConsumeContext<SendEmailCommandV1>>(c => c.Message == command && c.CancellationToken == CancellationToken.None);

//#if (Storage)
    private static SendEmailCommandV1 Command(string? attachmentKey = null, string? attachmentName = null) => new()
//#else
    private static SendEmailCommandV1 Command() => new()
//#endif
    {
        Id = Guid.NewGuid(),
        OccurredOnUtc = DateTimeOffset.UtcNow,
        ToEmail = "customer@example.test",
        Subject = "Your code",
        Body = "123456",
//#if (Storage)
        AttachmentKey = attachmentKey,
        AttachmentName = attachmentName,
//#endif
    };

    [Test(Description = "A command sent on the bus reaches its consumer, which sends the email")]
    public async Task Should_ConsumeTheCommand_SentOnTheBus()
    {
        await using var provider = new ServiceCollection()
            .AddSingleton(_email.Object)
//#if (Storage)
            .AddSingleton(_storage.Object)
//#endif
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMassTransitTestHarness(bus =>
            {
                // a command's queue has no service prefix: AddMessaging names it in kebab case, as here
                bus.SetKebabCaseEndpointNameFormatter();
                bus.AddConsumer<SendEmailCommandV1Consumer>();
            })
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // the address a sender's IMessageBus.SendAsync resolves for the command
        var queue = new Uri($"queue:{new KebabCaseEndpointNameFormatter(false).Message<SendEmailCommandV1>()}");
        await (await harness.Bus.GetSendEndpoint(queue)).Send(Command());

        (await harness.GetConsumerHarness<SendEmailCommandV1Consumer>().Consumed.Any<SendEmailCommandV1>()).ShouldBeTrue();
        _email.Verify(e => e.SendEmailAsync("customer@example.test", "Your code", "123456", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test(Description = "A command without an attachment goes out as the email it describes")]
    public async Task Should_SendTheEmail()
    {
        await Consumer().Consume(Context(Command()));

        _email.Verify(e => e.SendEmailAsync("customer@example.test", "Your code", "123456", CancellationToken.None), Times.Once);
        _email.VerifyNoOtherCalls();
    }
//#if (Storage)

    [Test(Description = "An attachment is read from object storage by its key and sent under its name")]
    public async Task Should_AttachTheFile_FromStorage()
    {
        var file = new byte[] { 1, 2, 3 };
        _storage.Setup(s => s.GetBytesAsync("invoices/42.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(file);

        await Consumer().Consume(Context(Command("invoices/42.pdf", "invoice-42.pdf")));

        _email.Verify(e => e.SendEmailWithAttachmentAsync("customer@example.test", "Your code", "123456", file,
            "invoice-42.pdf", CancellationToken.None), Times.Once);
        _email.VerifyNoOtherCalls();
    }

    [Test(Description = "A file that cannot be read leaves the email without the attachment, not unsent")]
    public async Task Should_SendWithoutTheAttachment_When_TheFileCannotBeRead()
    {
        _storage.Setup(s => s.GetBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no such key"));

        await Consumer().Consume(Context(Command("invoices/42.pdf", "invoice-42.pdf")));

        _email.Verify(e => e.SendEmailAsync("customer@example.test", "Your code", "123456", CancellationToken.None), Times.Once);
        _email.VerifyNoOtherCalls();
    }

    [Test(Description = "A send with the attachment that fails is repeated without it")]
    public async Task Should_SendWithoutTheAttachment_When_SendingItFails()
    {
        _storage.Setup(s => s.GetBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        _email.Setup(e => e.SendEmailWithAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("too large"));

        await Consumer().Consume(Context(Command("invoices/42.pdf", "invoice-42.pdf")));

        _email.Verify(e => e.SendEmailAsync("customer@example.test", "Your code", "123456", CancellationToken.None), Times.Once);
    }
//#endif
}
