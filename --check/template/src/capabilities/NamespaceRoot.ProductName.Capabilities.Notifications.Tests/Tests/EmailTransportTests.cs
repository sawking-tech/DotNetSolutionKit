using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using NamespaceRoot.ProductName.Capabilities.Notifications.Adapters;
using NamespaceRoot.ProductName.Capabilities.Notifications.Factories;
using NamespaceRoot.ProductName.Common.Tests.Stubs;

namespace NamespaceRoot.ProductName.Capabilities.Notifications.Tests;

/// <summary>
/// The SMTP and Graph transports with substituted clients: what they hand the client, how the connection is
/// secured, and the sandbox no transport can skip.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class EmailTransportTests
{
    private const string From = "noreply@example.com";
    private const string Sender = "mailbox@example.com";
    private const string SandboxRecipient = "sandbox@domain.com";
    private const string RealRecipient = "real-user@example.com";
    private const string Subject = "Test Subject";

    private static readonly TimeProvider Time = new TimeProviderMock(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static NotificationEmailSettings Settings(bool sandboxEnabled, int port = 587, bool secure = false) => new()
    {
        Provider = EmailProviderType.Smtp,
        TimeoutSeconds = 30,
        Smtp = new SmtpSettings { Host = "smtp.example.com", Port = port, From = From, Secure = secure },
        GraphApi = new GraphApiSettings { TenantId = "t", ClientId = "c", ClientSecret = "test-do-not-use", SenderEmail = Sender },
        Sandbox = new SandboxSettings { Enabled = sandboxEnabled, ReceiverAddress = SandboxRecipient },
    };

    private static IHostEnvironment Environment(string name) =>
        Mock.Of<IHostEnvironment>(e => e.EnvironmentName == name);

    private static (SmtpEmailTransport Transport, Mock<ISmtpClient> Client) Smtp(NotificationEmailSettings settings, string environment)
    {
        var client = new Mock<ISmtpClient>();
        client.Setup(c => c.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()))
            .ReturnsAsync("OK");
        var factory = new Mock<ISmtpClientFactory>();
        factory.Setup(f => f.Create()).Returns(client.Object);
        var transport = new SmtpEmailTransport(settings, Environment(environment), Time, factory.Object,
            NullLogger<SmtpEmailTransport>.Instance);
        return (transport, client);
    }

    [Test(Description = "SMTP sends to the recipient with the body as it was given, within the timeout of the settings")]
    public async Task Should_SendEmailSuccessfully()
    {
        var settings = Settings(sandboxEnabled: false);
        var (transport, client) = Smtp(settings, "Production");
        var timeout = 0;
        client.SetupSet(c => c.Timeout = It.IsAny<int>()).Callback<int>(t => timeout = t);

        await transport.SendAsync(RealRecipient, Subject, "Test body", CancellationToken.None);

        client.Verify(c => c.ConnectAsync("smtp.example.com", 587, It.IsAny<SecureSocketOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.SendAsync(
            It.Is<MimeMessage>(m => m.To.Mailboxes.Single().Address == RealRecipient && m.TextBody == "Test body"),
            It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()), Times.Once);
        timeout.ShouldBe(settings.TimeoutSeconds * 1000);
    }

    [TestCase(true, 587, SecureSocketOptions.SslOnConnect)]
    [TestCase(false, 465, SecureSocketOptions.SslOnConnect)]
    [TestCase(false, 587, SecureSocketOptions.StartTls)]
    [TestCase(false, 25, SecureSocketOptions.Auto)]
    public async Task Should_UseCorrectSecureSocketOption(bool secure, int port, SecureSocketOptions expected)
    {
        var (transport, client) = Smtp(Settings(sandboxEnabled: false, port, secure), "Production");

        await transport.SendAsync(RealRecipient, Subject, "Body", CancellationToken.None);

        client.Verify(c => c.ConnectAsync(It.IsAny<string>(), port, expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(true, "Development", SandboxRecipient)]
    [TestCase(false, "Development", SandboxRecipient)]
    [TestCase(true, "Production", SandboxRecipient)]
    [TestCase(false, "Production", RealRecipient)]
    public async Task Should_HandleSandboxCorrectly_OverSmtp(bool sandboxEnabled, string environment, string expected)
    {
        var (transport, client) = Smtp(Settings(sandboxEnabled), environment);
        var intercepted = expected == SandboxRecipient;

        await transport.SendAsync(RealRecipient, Subject, "Body", CancellationToken.None);

        client.Verify(c => c.SendAsync(
            It.Is<MimeMessage>(m =>
                m.To.Mailboxes.Single().Address == expected &&
                m.TextBody!.EndsWith("Body") &&
                m.TextBody!.Contains("[SANDBOX INTERCEPTED]") == intercepted &&
                m.TextBody!.Contains(RealRecipient) == intercepted),
            It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()), Times.Once);
    }

    [Test(Description = "An attachment goes out with its name, beside the body")]
    public async Task Should_SendTheAttachment_OverSmtp()
    {
        var (transport, client) = Smtp(Settings(sandboxEnabled: false), "Production");

        await transport.SendWithAttachmentAsync(RealRecipient, Subject, "Body", [1, 2, 3], "invoice.pdf", CancellationToken.None);

        client.Verify(c => c.SendAsync(
            It.Is<MimeMessage>(m => m.Attachments.OfType<MimePart>().Single().FileName == "invoice.pdf"),
            It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()), Times.Once);
    }

    [TestCase(true, "Development", SandboxRecipient)]
    [TestCase(false, "Development", SandboxRecipient)]
    [TestCase(false, "Production", RealRecipient)]
    public async Task Should_HandleSandboxCorrectly_OverGraph(bool sandboxEnabled, string environment, string expected)
    {
        var adapter = new Mock<IGraphClientAdapter>();
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.Create()).Returns(adapter.Object);
        var transport = new GraphApiEmailTransport(Settings(sandboxEnabled), Environment(environment), Time, factory.Object,
            NullLogger<GraphApiEmailTransport>.Instance);

        await transport.SendAsync(RealRecipient, Subject, "Body", CancellationToken.None);

        adapter.Verify(a => a.SendMailAsync(
            Sender, expected, Subject,
            It.Is<string>(body => body.EndsWith("Body") && body.Contains("[SANDBOX INTERCEPTED]") == (expected == SandboxRecipient)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
