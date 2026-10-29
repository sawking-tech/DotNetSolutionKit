using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Common.Application.Messaging.Consumers;
using NamespaceRoot.ProductName.Capabilities.Notifications;
using NamespaceRoot.ProductName.Common.Contracts.Messaging.Notifications;
using NamespaceRoot.ProductName.Common.Infrastructure.Messaging.Consumers;
//#if (Storage)
using NamespaceRoot.ProductName.Common.Infrastructure.Storage;
//#endif

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.Messaging.Consumers;

/// <summary>
/// Sends the email another service asked for, through the transport of this service's settings, so the
/// sandbox applies to it as to any other message.
/// </summary>
public sealed class SendEmailCommandV1Consumer(
    INotificationEmailSender emailSender,
//#if (Storage)
    IS3ObjectStorage storage,
//#endif
    ILogger<SendEmailCommandV1Consumer> logger)
    : BusCommandConsumer<SendEmailCommandV1>(logger)
{
    protected override async Task HandleAsync(IMessageContext<SendEmailCommandV1> context)
    {
        var command = context.Message;
//#if (Storage)
        var attachment = await TryReadAttachmentAsync(command, context.CancellationToken);

        if (attachment is { Length: > 0 })
        {
            try
            {
                await emailSender.SendEmailWithAttachmentAsync(command.ToEmail, command.Subject, command.Body,
                    attachment, command.AttachmentName!, context.CancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Email {CommandId} with attachment {AttachmentName} ({Size} bytes) failed; sending it without the attachment",
                    command.Id, command.AttachmentName, attachment.Length);
            }
        }
//#endif

        await emailSender.SendEmailAsync(command.ToEmail, command.Subject, command.Body, context.CancellationToken);
    }
//#if (Storage)

    // The email still goes out when its file is missing: the recipient learns of it from the text, while a
    // command retried for a file that is not there would only fail again.
    private async Task<byte[]?> TryReadAttachmentAsync(SendEmailCommandV1 command, CancellationToken ct)
    {
        if (command.AttachmentKey is null)
            return null;

        if (string.IsNullOrWhiteSpace(command.AttachmentName))
        {
            logger.LogError("Email {CommandId} has attachment {AttachmentKey} without a name; sending it without the attachment",
                command.Id, command.AttachmentKey);
            return null;
        }

        try
        {
            var file = await storage.GetBytesAsync(command.AttachmentKey, ct);
            if (file.Length == 0)
                logger.LogWarning("Attachment {AttachmentKey} of email {CommandId} is empty; sending it without the attachment",
                    command.AttachmentKey, command.Id);
            return file;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Attachment {AttachmentKey} of email {CommandId} could not be read; sending it without the attachment",
                command.AttachmentKey, command.Id);
            return null;
        }
    }
//#endif
}
