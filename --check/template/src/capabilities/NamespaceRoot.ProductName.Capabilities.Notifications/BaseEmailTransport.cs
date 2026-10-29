using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Capabilities.Notifications;

/// <summary>
/// The one way a message reaches a provider: the public methods apply the sandbox and only then call the
/// provider's own <see cref="ExecuteSendAsync"/>, so a new provider cannot forget it.
/// </summary>
internal abstract class BaseEmailTransport : IEmailTransport
{
    protected readonly INotificationEmailSettings Settings;
    private readonly IHostEnvironment _env;
    protected readonly ILogger<BaseEmailTransport> Logger;
    private readonly TimeProvider _timeProvider;

    protected BaseEmailTransport(
        INotificationEmailSettings settings,
        IHostEnvironment env,
        TimeProvider timeProvider,
        ILogger<BaseEmailTransport> logger)
    {
        Settings = settings;
        _env = env;
        _timeProvider = timeProvider;
        Logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct)
    {
        var (recipient, sent) = ApplySandbox(toEmail, body);
        await ExecuteSendAsync(recipient, subject, sent, ct);
    }

    public async Task SendWithAttachmentAsync(
        string toEmail, string subject, string body, byte[] attachment, string attachmentName, CancellationToken ct)
    {
        var (recipient, sent) = ApplySandbox(toEmail, body);
        await ExecuteSendWithAttachmentAsync(recipient, subject, sent, attachment, attachmentName, ct);
    }

    /// <summary>
    /// Outside Production, or while the sandbox is switched on, the message goes to the sandbox address with a
    /// note on top saying who it was for, the environment and the time.
    /// </summary>
    private (string recipient, string body) ApplySandbox(string toEmail, string body)
    {
        var sandbox = Settings.Sandbox.Enabled || !_env.IsProduction();
        if (!sandbox)
            return (toEmail, body);

        var recipient = Settings.Sandbox.ReceiverAddress;
        var note = $"""
            [SANDBOX INTERCEPTED]
            Original Recipient: {toEmail}
            Environment: {_env.EnvironmentName}
            UTC Time: {_timeProvider.GetUtcNow():yyyy-MM-dd HH:mm:ss}


            """;
        Logger.LogWarning("Sandbox: intercepted email for {Original} -> {Sandbox}", toEmail, recipient);
        return (recipient, note + body);
    }

    protected abstract Task ExecuteSendAsync(string toEmail, string subject, string body, CancellationToken ct);

    protected virtual Task ExecuteSendWithAttachmentAsync(
        string toEmail, string subject, string body, byte[] attachment, string attachmentName, CancellationToken ct)
        => ExecuteSendAsync(toEmail, subject, body, ct);
}
