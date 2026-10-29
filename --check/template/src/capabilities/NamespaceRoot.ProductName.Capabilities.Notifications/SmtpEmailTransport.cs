using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
using NamespaceRoot.ProductName.Capabilities.Notifications.Factories;

namespace NamespaceRoot.ProductName.Capabilities.Notifications;

internal sealed class SmtpEmailTransport : BaseEmailTransport
{
    private readonly ISmtpClientFactory _smtpClientFactory;

    public SmtpEmailTransport(
        INotificationEmailSettings settings,
        IHostEnvironment env,
        TimeProvider timeProvider,
        ISmtpClientFactory smtpClientFactory,
        ILogger<SmtpEmailTransport> logger) : base(settings, env, timeProvider, logger)
    {
        _smtpClientFactory = smtpClientFactory;
    }

    protected override Task ExecuteSendAsync(string toEmail, string subject, string body, CancellationToken ct)
        => SendSmtpAsync(toEmail, subject, body, ct, attachment: null, attachmentName: null);

    protected override Task ExecuteSendWithAttachmentAsync(
        string toEmail, string subject, string body, byte[] attachment, string attachmentName, CancellationToken ct)
        => SendSmtpAsync(toEmail, subject, body, ct, attachment, attachmentName);

    private async Task SendSmtpAsync(
        string toEmail, string subject, string body, CancellationToken ct, byte[]? attachment, string? attachmentName)
    {
        var smtp = Settings.Smtp ?? throw new InvalidOperationException("SMTP configuration is missing.");

        var bodyBuilder = new BodyBuilder { TextBody = body };
        if (attachment is not null && attachmentName is not null)
            bodyBuilder.Attachments.Add(attachmentName, attachment);

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp.From));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = bodyBuilder.ToMessageBody();

        try
        {
            using var client = _smtpClientFactory.Create();
            client.Timeout = Settings.TimeoutSeconds * 1000;

            var secure = smtp.Secure ? SecureSocketOptions.SslOnConnect
                : smtp.Port switch { 465 => SecureSocketOptions.SslOnConnect, 587 => SecureSocketOptions.StartTls, _ => SecureSocketOptions.Auto };

            await client.ConnectAsync(smtp.Host, smtp.Port, secure, ct);

            if (!string.IsNullOrWhiteSpace(smtp.Username) && !string.IsNullOrWhiteSpace(smtp.Password))
                await client.AuthenticateAsync(smtp.Username, smtp.Password, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            Logger.LogInformation("Email sent via SMTP to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "SMTP delivery failed for {Email}", toEmail);
            throw;
        }
    }
}
