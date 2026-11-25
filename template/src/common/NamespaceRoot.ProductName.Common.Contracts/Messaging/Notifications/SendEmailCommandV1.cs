using NamespaceRoot.ProductName.Common.Domain.Messaging;

namespace NamespaceRoot.ProductName.Common.Contracts.Messaging.Notifications;

/// <summary>
/// Asks the service that owns notifications to send an email. Any service sends it; the owner, the one
/// generated with <c>--Notify email</c>, consumes it and sends through its transport, sandbox included.
/// </summary>
/// <remarks>
/// The message goes out as it is: a subject and plain text.
//#if (Storage)
/// An attachment travels as a key in object storage, not as bytes in the message: the sender puts the file
/// there first. When the file cannot be read, the email goes out without it.
//#endif
/// </remarks>
// TODO: the attachment fields below are cut out of a kernel contract by the Storage conditional - the
// variant cut ADR-009's Context rejects. Pre-existing (the same on master): the contract predates the
// module split, and the message without Storage is the same record minus two nullable fields. Goes away
// with either the attachments moving to their own message or Storage becoming always-on for
// notifications. The word is spelled out: a conditional directive inside a comment would be read by the
// template engine as one.
public sealed record SendEmailCommandV1 : IBusCommand
{
    public required Guid Id { get; init; }
    public required DateTimeOffset OccurredOnUtc { get; init; }

    public required string ToEmail { get; init; }
    public required string Subject { get; init; }

    /// <summary>Plain text.</summary>
    public required string Body { get; init; }
//#if (Storage)

    /// <summary>The key of the attachment in object storage; null for an email without one.</summary>
    public string? AttachmentKey { get; init; }

    /// <summary>The file name the recipient sees; required with <see cref="AttachmentKey"/>.</summary>
    public string? AttachmentName { get; init; }
//#endif
}
