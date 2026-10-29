using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;

namespace NamespaceRoot.ProductName.Capabilities.Notifications.Adapters;

/// <summary>The Graph SDK behind an interface: the transport is tested without it.</summary>
internal interface IGraphClientAdapter
{
    Task SendMailAsync(string senderEmail, string toEmail, string subject, string body, CancellationToken ct);

    Task SendMailWithAttachmentAsync(
        string senderEmail, string toEmail, string subject, string body, byte[] attachment, string attachmentName,
        CancellationToken ct);
}

internal sealed class GraphClientAdapter : IGraphClientAdapter
{
    private readonly GraphServiceClient _client;

    public GraphClientAdapter(string tenantId, string clientId, string clientSecret)
    {
        _client = new GraphServiceClient(new ClientSecretCredential(tenantId, clientId, clientSecret));
    }

    public Task SendMailAsync(string senderEmail, string toEmail, string subject, string body, CancellationToken ct)
        => SendGraphMailAsync(senderEmail, toEmail, subject, body, ct, attachment: null, attachmentName: null);

    public Task SendMailWithAttachmentAsync(
        string senderEmail, string toEmail, string subject, string body, byte[] attachment, string attachmentName,
        CancellationToken ct)
        => SendGraphMailAsync(senderEmail, toEmail, subject, body, ct, attachment, attachmentName);

    private async Task SendGraphMailAsync(
        string senderEmail, string toEmail, string subject, string body, CancellationToken ct, byte[]? attachment,
        string? attachmentName)
    {
        var message = new Message
        {
            Subject = subject,
            Body = new ItemBody { ContentType = BodyType.Text, Content = body },
            ToRecipients = [new Recipient { EmailAddress = new EmailAddress { Address = toEmail } }],
        };

        if (attachment is not null && attachmentName is not null)
        {
            message.Attachments =
            [
                new FileAttachment
                {
                    Name = attachmentName,
                    ContentBytes = attachment,
                    // The type of the file by its name, as SMTP sends it.
                    ContentType = MimeTypes.GetMimeType(attachmentName),
                },
            ];
        }

        try
        {
            await _client.Users[senderEmail]
                .SendMail
                .PostAsync(new SendMailPostRequestBody { Message = message, SaveToSentItems = false }, null, ct);
        }
        catch (ODataError ex)
        {
            throw new InvalidOperationException($"Graph API error: {ex.Error?.Code} - {ex.Error?.Message}", ex);
        }
    }
}
