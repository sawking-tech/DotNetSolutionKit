namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;

/// <summary>
/// Infisical as the secret store: the <c>Infisical</c> section.
/// </summary>
public sealed class InfisicalOptions : SecretStoreOptions
{
    public const string SectionName = "Infisical";

    /// <summary>
    /// The Infisical instance, for example <c>https://app.infisical.com</c>.
    /// </summary>
    public string HostUri { get; set; } = "https://app.infisical.com";

    /// <summary>
    /// The project holding the platform's secrets.
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Which environment to read: <c>dev</c>, <c>staging</c> or <c>prod</c>.
    /// </summary>
    public string EnvironmentSlug { get; set; } = string.Empty;

    /// <inheritdoc />
    public override string SharedPath { get; set; } = "/";

    /// <summary>
    /// Machine identity client id. Supplied through the environment.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Machine identity client secret. Supplied through the environment.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId)
        && !string.IsNullOrWhiteSpace(EnvironmentSlug)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}
