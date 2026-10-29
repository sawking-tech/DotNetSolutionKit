using MailKit.Net.Smtp;

namespace NamespaceRoot.ProductName.Capabilities.Notifications.Factories;

internal interface ISmtpClientFactory
{
    ISmtpClient Create();
}

internal sealed class SmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create() => new SmtpClient();
}
