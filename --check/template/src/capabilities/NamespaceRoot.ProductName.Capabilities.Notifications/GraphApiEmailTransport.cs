using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Capabilities.Notifications.Factories;

namespace NamespaceRoot.ProductName.Capabilities.Notifications;

internal sealed class GraphApiEmailTransport : BaseEmailTransport
{
    private readonly IGraphClientFactory _graphClientFactory;

    public GraphApiEmailTransport(
        INotificationEmailSettings settings,
        IHostEnvironment env,
        TimeProvider timeProvider,
        IGraphClientFactory graphClientFactory,
        ILogger<GraphApiEmailTransport> logger) : base(settings, env, timeProvider, logger)
    {
        _graphClientFactory = graphClientFactory;
    }

    protected override Task ExecuteSendAsync(string toEmail, string subject, string body, CancellationToken ct)
        => SendGraphAsync(toEmail, subject, body, ct, attachment: null, attachmentName: null);

    protected override Task ExecuteSendWithAttachmentAsync(
        string toEmail, string subject, string body, byte[] attachment, string attachmentName, CancellationToken ct)
        => SendGraphAsync(toEmail, subject, body, ct, attachment, attachmentName);

    private async Task SendGraphAsync(
        string toEmail, string subject, string body, CancellationToken ct, byte[]? attachment, string? attachmentName)
    {
        var graph = Settings.GraphApi ?? throw new InvalidOperationException("GraphApi configuration is missing.");
        var adapter = _graphClientFactory.Create();

        try
        {
            if (attachment is not null && attachmentName is not null)
                await adapter.SendMailWithAttachmentAsync(graph.SenderEmail, toEmail, subject, body, attachment, attachmentName, ct);
            else
                await adapter.SendMailAsync(graph.SenderEmail, toEmail, subject, body, ct);

            Logger.LogInformation("Email sent via Graph API to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Graph API delivery failed for {Email}", toEmail);
            throw;
        }
    }
}
