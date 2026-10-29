namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;

/// <summary>
/// HashiCorp Vault as the secret store: the <c>Vault</c> section.
/// </summary>
/// <remarks>
/// The secrets are key-value secrets of a KV version 2 engine: the shared one and the service's own, each
/// a set of keys. A service signs in with a token or with an AppRole; both come from the environment.
/// </remarks>
public sealed class VaultOptions : SecretStoreOptions
{
    public const string SectionName = "Vault";

    /// <summary>
    /// Where Vault runs, for example <c>https://vault.example.com:8200</c>.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// The mount of the KV version 2 engine holding the secrets.
    /// </summary>
    public string Mount { get; set; } = "secret";

    /// <inheritdoc />
    public override string SharedPath { get; set; } = "shared";

    /// <summary>
    /// A token to sign in with. Supplied through the environment; leave empty to sign in with an AppRole.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// The AppRole's role id, with <see cref="SecretId"/>. Supplied through the environment.
    /// </summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary>
    /// The AppRole's secret id, with <see cref="RoleId"/>. Supplied through the environment.
    /// </summary>
    public string SecretId { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Address)
        && (!string.IsNullOrWhiteSpace(Token)
            || (!string.IsNullOrWhiteSpace(RoleId) && !string.IsNullOrWhiteSpace(SecretId)));
}
