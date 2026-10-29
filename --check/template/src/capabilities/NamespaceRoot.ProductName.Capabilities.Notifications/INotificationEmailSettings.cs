namespace NamespaceRoot.ProductName.Capabilities.Notifications;

/// <summary>How email is sent, the <c>Email</c> section.</summary>
public interface INotificationEmailSettings
{
    EmailProviderType Provider { get; }

    int TimeoutSeconds { get; }

    INotificationSmtpSettings? Smtp { get; }

    INotificationGraphApiSettings? GraphApi { get; }

    INotificationSandboxSettings Sandbox { get; }
}

public interface INotificationSmtpSettings
{
    string Host { get; }

    int Port { get; }

    string From { get; }

    string? Username { get; }

    string? Password { get; }

    /// <summary>TLS on connect whatever the port; otherwise 465 is TLS on connect and 587 STARTTLS.</summary>
    bool Secure { get; }
}

public interface INotificationGraphApiSettings
{
    string TenantId { get; }

    string ClientId { get; }

    string ClientSecret { get; }

    /// <summary>The mailbox of the organisation messages are sent from.</summary>
    string SenderEmail { get; }
}

public interface INotificationSandboxSettings
{
    /// <summary>
    /// Diverts every message to <see cref="ReceiverAddress"/>. Outside Production messages are diverted
    /// whatever this says.
    /// </summary>
    bool Enabled { get; }

    string ReceiverAddress { get; }
}

public enum EmailProviderType
{
    Unknown = 0,
    Smtp = 1,
    GraphApi = 2,
}
