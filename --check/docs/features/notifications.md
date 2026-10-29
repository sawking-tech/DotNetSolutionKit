# Notifications

`--Notify` gives a service notifications and chooses their channels; `--Notify email` is email, and SignalR
is [planned](https://github.com/sawking-tech/DotNetSolutionKit/issues/71) as a second channel. The flag goes
to the service that owns notifications. A message goes to its recipient as it is: a subject and plain text.
Messages rendered from templates come with `--Templates`, which is
[planned](https://github.com/sawking-tech/DotNetSolutionKit/issues/74) on top of `--Documents`.

| Part | |
|---|---|
| `INotificationEmailSender` | the port in `Capabilities.Notifications`, a project of its own that a service with `--Notify email` references; it needs nothing else of `Common`: `SendEmailAsync` and `SendEmailWithAttachmentAsync` |
| transports | SMTP on MailKit and Microsoft Graph, internal; the one `Email:Provider` names is chosen once, at registration |
| sandbox | outside Production every message goes to one address, with a note on top naming who it was for, the environment and the time; no transport can skip it |

```csharp
public sealed class InvoiceMailer(INotificationEmailSender email) : IInvoiceMailer
{
    public Task SendAsync(Invoice invoice, byte[] pdf, CancellationToken ct) =>
        email.SendEmailWithAttachmentAsync(invoice.CustomerEmail, $"Invoice {invoice.Number}",
            "Your invoice is attached.", pdf, $"{invoice.Number}.pdf", ct);
}
```

Graph takes the attachment in the same request as the message. Microsoft documents this way for files
under 3 MB; a larger file needs an
[upload session](https://learn.microsoft.com/en-us/graph/outlook-large-attachments), which the transport
does not open; for a larger attachment use SMTP.

## Asking from another service

With `--Messaging`, another service asks for an email with the bus command `SendEmailCommandV1` from
`Common.Contracts`; nothing calls the owner synchronously. Its consumer, `SendEmailCommandV1Consumer` in the
owner's Infrastructure, sends the message through the port, so the sandbox applies to it too. With
`--Storage` the command carries an attachment as a key in object storage and a file name: the sender puts the
file there first, and when it cannot be read, or sending with it fails, the email goes out without it. The
sender and the owner read the same bucket, `S3:BucketName`.

Every service generated with both `--Notify email` and `--Messaging` gets the consumer, and the command's queue
has no service prefix: two such services share the commands between them, each sending with its own `Email`
settings. Keep the consumer in one service; a service that only sends through its own port does not need it.

```csharp
await bus.SendAsync(new SendEmailCommandV1
{
    Id = Guid.NewGuid(),
    OccurredOnUtc = time.GetUtcNow(),
    ToEmail = customer.Email,
    Subject = $"Invoice {invoice.Number}",
    Body = "Your invoice is attached.",
    AttachmentKey = $"invoices/{invoice.Id}.pdf",
    AttachmentName = $"{invoice.Number}.pdf",
}, ct);
```

## The sandbox

A test stand must not mail anyone outside. Every transport derives from one base whose methods apply the
sandbox and only then hand the message to the provider: outside Production, or while
`Email:Sandbox:Enabled` is `true`, the message goes to `Email:Sandbox:ReceiverAddress` with a note on top
(`[SANDBOX INTERCEPTED]`, the original recipient, the environment, the time in UTC). Production sends to the
real recipients only with the sandbox switched off.

## Settings

Checked at startup: the provider has to be named, its section has what it needs, and the addresses (`From`,
`SenderEmail`, `Sandbox:ReceiverAddress`) are email addresses. An SMTP sign-in has both `Username` and
`Password` or neither: with half of it the transport would skip the sign-in and every send would fail.

| Setting | |
|---|---|
| `Email:Provider` | `Smtp` or `GraphApi` |
| `Email:TimeoutSeconds` | 30: SMTP, for each network operation of a delivery; Graph does not read it |
| `Email:Smtp:Host`, `Email:Smtp:Port`, `Email:Smtp:From` | the server and the sender; port 587 by default |
| `Email:Smtp:Secure` | `true`: TLS on connect on any port; otherwise 465 is TLS on connect and 587 STARTTLS |
| `Email:Smtp:Username`, `Email:Smtp:Password` | only for a server that wants a sign-in, from the secret store |
| `Email:GraphApi:TenantId`, `ClientId`, `ClientSecret` | the application that sends, the secret from the secret store |
| `Email:GraphApi:SenderEmail` | the mailbox of the organisation messages are sent from |
| `Email:Sandbox:Enabled` | `true` by default; outside Production the sandbox applies whatever it says |
| `Email:Sandbox:ReceiverAddress` | where diverted messages go |

A mail server that is down does not take the service out of traffic: email has no readiness check, and a
failed delivery fails the call that asked for it.

Under docker compose the infrastructure gets [MailHog](https://github.com/mailhog/MailHog), which catches
every message and shows it at `http://127.0.0.1:8025`; `SMTP_HOST` and the other `SMTP_*` and `EMAIL_*` values
in `deploy/compose/.env` name a real server instead.

## Tests

A service test gives the service a double of `INotificationEmailSender`, as of any other port; the owner's
tests check that a command sent on the bus to its queue reaches the consumer of `SendEmailCommandV1`, which sends
what the command says, with its attachment and without it.
`Capabilities.Notifications.Tests` checks the core: the settings it requires, the transport of the named provider, what the SMTP
and Graph transports hand their clients, the security by port and the sandbox; and, against a real MailHog
named by `TEST_SMTP` and `TEST_SMTP_API`, that a message arrives at its recipient in Production and at the
sandbox address elsewhere.

```bash
eval "$(bash tests/servers/up.sh)"     # the test servers, MailHog among them (tests/servers/mailhog.sh)
dotnet test --filter "FullyQualifiedName~Smtp"
```
