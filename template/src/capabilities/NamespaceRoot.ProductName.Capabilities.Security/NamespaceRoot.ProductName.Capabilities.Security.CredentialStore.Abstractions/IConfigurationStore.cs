namespace NamespaceRoot.ProductName.Capabilities.Security.CredentialStore.Abstractions;

/// <summary>
/// Reads the configuration a service takes from its store.
/// </summary>
/// <remarks>
/// An interface rather than a direct call to the vendor's client, so that how the store becomes configuration
/// - which folder wins, how a key becomes a configuration path, what happens when the store is
/// unreachable - is testable without a network or a token. Those rules are where the mistakes are; the
/// HTTP call is the part that is already written for us.
/// </remarks>
public interface IConfigurationStore
{
    /// <summary>
    /// Reads every value in one folder.
    /// </summary>
    /// <param name="path">The folder, for example <c>/</c> or <c>/auth</c>.</param>
    /// <returns>Key names and their values. Empty when the folder holds nothing.</returns>
    Task<IReadOnlyDictionary<string, string>> ReadAsync(string path, CancellationToken cancellationToken = default);
}
