using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Capabilities.Notifications;

public sealed class NotificationEmailSettings : INotificationEmailSettings, IValidatableObject
{
    public const string SectionName = "Email";

    [Range(1, int.MaxValue, ErrorMessage = "Email Provider must be set to GraphApi or Smtp.")]
    public EmailProviderType Provider { get; set; } = EmailProviderType.Unknown;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    public SmtpSettings? Smtp { get; set; }

    public GraphApiSettings? GraphApi { get; set; }

    public SandboxSettings Sandbox { get; set; } = new();

    INotificationSmtpSettings? INotificationEmailSettings.Smtp => Smtp;
    INotificationGraphApiSettings? INotificationEmailSettings.GraphApi => GraphApi;
    INotificationSandboxSettings INotificationEmailSettings.Sandbox => Sandbox;

    /// <summary>
    /// The section of the chosen provider, checked at startup as the top level is: data annotations do not
    /// reach into a nested object on their own.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        object? section = Provider switch
        {
            EmailProviderType.Smtp => Smtp,
            EmailProviderType.GraphApi => GraphApi,
            _ => null,
        };
        foreach (var result in Check(Sandbox, "Sandbox"))
            yield return result;
        if (Provider == EmailProviderType.Unknown)
            yield break;
        if (section is null)
        {
            yield return new ValidationResult($"Email:{Provider} is required when Email:Provider is {Provider}.");
            yield break;
        }

        foreach (var result in Check(section, Provider.ToString()))
            yield return result;

        // A sign-in with half its secret would be skipped silently, and every send would fail later.
        if (section is SmtpSettings smtp && string.IsNullOrWhiteSpace(smtp.Username) != string.IsNullOrWhiteSpace(smtp.Password))
            yield return new ValidationResult("Email:Smtp: Username and Password are set together or not at all.");
    }

    private static IEnumerable<ValidationResult> Check(object section, string name)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(section, new ValidationContext(section), results, validateAllProperties: true);
        return results.Select(r => new ValidationResult($"Email:{name}: {r.ErrorMessage}", r.MemberNames));
    }
}

public sealed class SmtpSettings : INotificationSmtpSettings
{
    [Required] public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    [Required, EmailAddress] public string From { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool Secure { get; set; }
}

public sealed class GraphApiSettings : INotificationGraphApiSettings
{
    [Required] public string TenantId { get; set; } = string.Empty;
    [Required] public string ClientId { get; set; } = string.Empty;
    [Required] public string ClientSecret { get; set; } = string.Empty;
    [Required, EmailAddress] public string SenderEmail { get; set; } = string.Empty;
}

public sealed class SandboxSettings : INotificationSandboxSettings
{
    public bool Enabled { get; set; } = true;
    [Required, EmailAddress] public string ReceiverAddress { get; set; } = "sandbox@example.com";
}
