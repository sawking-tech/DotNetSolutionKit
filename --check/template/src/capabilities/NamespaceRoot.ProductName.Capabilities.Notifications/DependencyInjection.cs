using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Capabilities.Notifications.Factories;

namespace NamespaceRoot.ProductName.Capabilities.Notifications;

public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="INotificationEmailSender"/> from the <c>Email</c> section, checked at startup. The
    /// transport of the provider it names is chosen here, once: the provider does not change while the service
    /// runs, and one the settings do not name is never built.
    /// </summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<NotificationEmailSettings>()
            .BindConfiguration(NotificationEmailSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<INotificationEmailSettings>(sp => sp.GetRequiredService<IOptions<NotificationEmailSettings>>().Value);
        services.TryAddSingleton(TimeProvider.System);

        var provider = configuration.GetValue($"{NotificationEmailSettings.SectionName}:Provider", EmailProviderType.Unknown);
        switch (provider)
        {
            case EmailProviderType.GraphApi:
                services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
                services.AddScoped<IEmailTransport, GraphApiEmailTransport>();
                break;
            default:
                // Smtp, and an unknown provider too: the startup check refuses it before the first message.
                services.AddSingleton<ISmtpClientFactory, SmtpClientFactory>();
                services.AddScoped<IEmailTransport, SmtpEmailTransport>();
                break;
        }

        services.AddScoped<INotificationEmailSender, NotificationEmailService>();
        return services;
    }
}

/// <summary>Sends through the transport registered for the configured provider.</summary>
internal sealed class NotificationEmailService(IEmailTransport transport) : INotificationEmailSender
{
    public Task SendEmailAsync(string toEmail, string subject, string body, CancellationToken ct)
        => transport.SendAsync(toEmail, subject, body, ct);

    public Task SendEmailWithAttachmentAsync(
        string toEmail, string subject, string body, byte[] attachment, string attachmentName, CancellationToken ct)
        => transport.SendWithAttachmentAsync(toEmail, subject, body, attachment, attachmentName, ct);
}
