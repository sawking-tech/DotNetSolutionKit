// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using Infisical.Sdk;
using Infisical.Sdk.Model;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;

/// <summary>
/// Reads secrets from Infisical with a machine identity.
/// </summary>
/// <remarks>
/// The client logs in once and is reused: authentication is a round trip, and a service reads several
/// folders while starting up.
/// </remarks>
public sealed class InfisicalSecretStore : ISecretStore, IDisposable
{
    private readonly InfisicalOptions _options;
    private readonly SemaphoreSlim _loginGate = new(1, 1);

    private InfisicalClient? _client;

    public InfisicalSecretStore(InfisicalOptions options)
    {
        _options = options;
    }

    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var client = await AuthenticatedClientAsync(cancellationToken);

        Secret[] secrets;
        try
        {
            secrets = await client.Secrets().ListAsync(new ListSecretsOptions
            {
                ProjectId = _options.ProjectId,
                EnvironmentSlug = _options.EnvironmentSlug,
                SecretPath = path,
                ViewSecretValue = true,

                // References let one secret point at another; resolving them here means a service reads a
                // value rather than a pointer it would have to understand.
                ExpandSecretReferences = true,

                // Folders are read one at a time, deliberately: recursion would pull another service's
                // folder into this service's configuration and quietly widen what it can see.
                Recursive = false,
            });
        }
        catch (Exception exception)
        {
            throw new ConfigurationException(
                $"Failed to read secrets from '{path}' in environment '{_options.EnvironmentSlug}'.",
                exception);
        }

        return secrets
            .Where(secret => secret.SecretKey.Length > 0)
            .ToDictionary(secret => secret.SecretKey, secret => secret.SecretValue ?? string.Empty);
    }

    public void Dispose() => _loginGate.Dispose();

    private async Task<InfisicalClient> AuthenticatedClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _loginGate.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var client = new InfisicalClient(new InfisicalSdkSettingsBuilder()
                .WithHostUri(_options.HostUri)
                .Build());

            try
            {
                await client.Auth().UniversalAuth().LoginAsync(_options.ClientId, _options.ClientSecret);
            }
            catch (Exception exception)
            {
                // The message deliberately names neither the identity nor the secret: it is written to the
                // log of a service that failed to start, and that log is not a place for credentials.
                throw new ConfigurationException(
                    $"Failed to authenticate against the secret store at '{_options.HostUri}'.", exception);
            }

            _client = client;

            return client;
        }
        finally
        {
            _loginGate.Release();
        }
    }
}
