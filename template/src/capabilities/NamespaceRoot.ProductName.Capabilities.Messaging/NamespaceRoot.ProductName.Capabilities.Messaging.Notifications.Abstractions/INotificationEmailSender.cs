namespace NamespaceRoot.ProductName.Capabilities.Messaging.Notifications.Abstractions;

/// <summary>
/// Sends a message by email to its recipient as it is: a subject and a plain text body. Implementations use
/// SMTP or Graph API.
/// </summary>
public interface INotificationEmailSender
{
    Task SendEmailAsync(string toEmail, string subject, string body, CancellationToken ct);

    Task SendEmailWithAttachmentAsync(
        string toEmail, string subject, string body, byte[] attachment, string attachmentName, CancellationToken ct);
}
