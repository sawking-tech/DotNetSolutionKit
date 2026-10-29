namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;

/// <summary>
/// Reads secrets from wherever they are kept.
/// </summary>
/// <remarks>
/// An interface rather than a direct call to the vendor's client, so that how secrets become configuration
/// - which folder wins, how a key becomes a configuration path, what happens when the store is
/// unreachable - is testable without a network or a token. Those rules are where the mistakes are; the
/// HTTP call is the part that is already written for us.
/// </remarks>
public interface ISecretStore
{
    /// <summary>
    /// Reads every secret in one folder.
    /// </summary>
    /// <param name="path">The folder, for example <c>/</c> or <c>/auth</c>.</param>
    /// <returns>Secret names and their values. Empty when the folder holds nothing.</returns>
    Task<IReadOnlyDictionary<string, string>> ReadAsync(string path, CancellationToken cancellationToken = default);
}
