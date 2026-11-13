# Notifications (Email)

`Capabilities.Messaging.Notifications` sends the solution's email: through SMTP, through Microsoft
Graph, or to a sandbox that writes the letter instead of sending it. A service asks for one
interface, `INotificationEmailSender`, and the configuration of the solution decides the transport.

```csharp
services.AddNotifications(configuration);
...
await _emailSender.SendEmailAsync(to, subject, body, cancellationToken);
```

### What is inside

- `DependencyInjection.AddNotifications` - reads the `Email` section, picks the transport its
  `Provider` names and registers `INotificationEmailSender`.
- `SmtpEmailTransport` / `GraphApiEmailTransport` - the two real transports; `BaseEmailTransport`
  holds what they share.
- `NotificationEmailSettings` / `SmtpSettings` / `GraphApiSettings` / `SandboxSettings` - the typed
  settings, validated at startup.
- Adapters and factories (`IGraphClientAdapter`, `ISmtpClientFactory`, `IGraphClientFactory`) - the
  external clients are behind an interface, so the module is testable without them.
- `Notifications.Abstractions` - `INotificationEmailSender` and the settings interfaces; a consumer
  references the contracts and takes no SDK transitively.

What is deliberately not here: the letters themselves and the decision to send one - a template, a
recipient list and a moment are the product's.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
