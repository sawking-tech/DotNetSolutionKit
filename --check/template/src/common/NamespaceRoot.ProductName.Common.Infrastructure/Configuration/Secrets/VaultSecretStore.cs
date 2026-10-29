using System.Net;
using System.Text.Json;
using NamespaceRoot.ProductName.Common.Exceptions;
using VaultSharp;
using VaultSharp.Core;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Token;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;

/// <summary>
/// Reads secrets from the KV version 2 engine of HashiCorp Vault.
/// </summary>
/// <remarks>
/// A path is one secret, and its keys are the configuration values, as a folder of secrets is in
/// Infisical. A path that does not exist reads as empty, as an empty folder does: a service without
/// secrets of its own needs no secret created for it. A refusal or an unreachable Vault is a
/// <see cref="ConfigurationException"/>, which the configuration provider answers with a refusal to start.
/// </remarks>
public sealed class VaultSecretStore : ISecretStore
{
    private readonly VaultOptions _options;
    private readonly Lazy<IVaultClient> _client;

    public VaultSecretStore(VaultOptions options)
    {
        _options = options;
        _client = new Lazy<IVaultClient>(() => new VaultClient(new VaultClientSettings(options.Address, SignIn(options))));
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var secret = await _client.Value.V1.Secrets.KeyValue.V2.ReadSecretAsync(path.Trim('/'), mountPoint: _options.Mount);
            return secret.Data.Data.ToDictionary(pair => pair.Key, pair => Text(pair.Value));
        }
        catch (VaultApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return new Dictionary<string, string>();
        }
        catch (Exception exception) when (exception is not ConfigurationException)
        {
            // The message names the path and the address, never the token: it is written to the log of a
            // service that failed to start.
            throw new ConfigurationException(
                $"Failed to read secrets from '{_options.Mount}/{path}' at '{_options.Address}'.", exception);
        }
    }

    private static IAuthMethodInfo SignIn(VaultOptions options) =>
        !string.IsNullOrWhiteSpace(options.Token)
            ? new TokenAuthMethodInfo(options.Token)
            : new AppRoleAuthMethodInfo(options.RoleId, options.SecretId);

    // A value is a JSON value: a string as it is, anything else as its JSON text.
    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => value.ToString() ?? string.Empty,
    };
}
