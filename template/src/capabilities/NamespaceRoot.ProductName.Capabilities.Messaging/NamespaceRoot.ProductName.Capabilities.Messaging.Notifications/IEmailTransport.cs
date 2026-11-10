namespace NamespaceRoot.ProductName.Capabilities.Messaging.Notifications;

internal interface IEmailTransport
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct);

    Task SendWithAttachmentAsync(
        string toEmail, string subject, string body, byte[] attachment, string attachmentName, CancellationToken ct);
}
