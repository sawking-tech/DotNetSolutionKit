using Microsoft.Extensions.Configuration;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;

/// <summary>
/// Makes the secret store one more configuration source, so nothing downstream knows a value came from it.
/// </summary>
/// <remarks>
/// Secrets are read from the shared folder first and the service's own folder second, so a service can
/// override a shared value. A secret name maps to a configuration path by the usual convention -
/// <c>ConnectionStrings__DefaultConnection</c> becomes <c>ConnectionStrings:DefaultConnection</c> -
/// which is the same spelling environment variables already use.
/// </remarks>
public sealed class SecretsConfigurationProvider : ConfigurationProvider, IDisposable
{
    /// <summary>
    /// When and why the last reload could not read the store; the service keeps the values it had.
    /// </summary>
    public const string ReloadErrorKey = "Secrets:ReloadError";

    private static readonly string[] StatusKeys = [ReloadErrorKey];

    private readonly SecretStoreOptions _options;
    private readonly ISecretStore _store;
    private Timer? _reloadTimer;
    private int _reloading;

    public SecretsConfigurationProvider(SecretStoreOptions options, ISecretStore store)
    {
        _options = options;
        _store = store;
    }

    public override void Load()
    {
        // Configuration is built synchronously, before the host exists. Blocking here is deliberate: a
        // service must not reach its first request before it knows whether it has its secrets.
        LoadAsync().GetAwaiter().GetResult();
        StartReloading();
    }

    /// <summary>
    /// Reads the store every <see cref="SecretStoreOptions.ReloadSeconds"/>, so a value changed there reaches
    /// the running service: whatever reads it through <c>IOptionsMonitor</c> or <c>IReloadable</c> sees the
    /// new value, and what was built from it at startup keeps the old one until a restart.
    /// </summary>
    private void StartReloading()
    {
        if (_reloadTimer is not null || !_options.IsConfigured || _options.ReloadSeconds <= 0)
            return;

        var period = TimeSpan.FromSeconds(_options.ReloadSeconds);
        _reloadTimer = new Timer(_ => _ = ReloadAsync(), null, period, period);
    }

    /// <summary>
    /// One reload: new values replace the old and raise the change token; a store that does not answer
    /// leaves the values as they are and says so in <see cref="ReloadErrorKey"/>.
    /// </summary>
    internal async Task ReloadAsync()
    {
        // A slow store must not stack reloads on top of each other.
        if (Interlocked.Exchange(ref _reloading, 1) == 1)
            return;

        try
        {
            Dictionary<string, string?> loaded;
            try
            {
                loaded = await ReadStoreAsync();
            }
            catch (ConfigurationException exception)
            {
                Data = new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase)
                {
                    [ReloadErrorKey] = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC: {exception.Message}",
                };
                return;
            }

            var changed = !SameValues(loaded, Data);
            Data = loaded;
            if (changed)
                RaiseReload();
        }
        finally
        {
            Volatile.Write(ref _reloading, 0);
        }
    }

    public void Dispose() => _reloadTimer?.Dispose();

    // A listener that throws, such as options whose new value fails validation, must not stop the reloads
    // that come after: the values are already in, and the next change raises the token again.
    private void RaiseReload()
    {
        try
        {
            OnReload();
        }
        catch (Exception)
        {
        }
    }

    private static bool SameValues(IDictionary<string, string?> next, IDictionary<string, string?> current)
    {
        var values = current.Where(pair => !StatusKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        var nextValues = next.Where(pair => !StatusKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        return values.Count == nextValues.Count
               && nextValues.All(pair => current.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    private async Task LoadAsync()
    {
        if (!_options.IsConfigured)
        {
            if (!_options.Optional)
            {
                throw new ConfigurationException(
                    "The secret store is not configured: project, environment and machine identity are all required.");
            }

            // Nothing configured and explicitly optional - a local run reading its values from files.
            return;
        }

        Dictionary<string, string?> loaded;
        try
        {
            loaded = await ReadStoreAsync();
        }
        catch (ConfigurationException)
        {
            // The store was configured but could not be read. The secrets live there and in the memory of a
            // running service, nowhere else, so a service that starts now has none: running without the store
            // is allowed only where the service was told it may.
            if (_options.Optional)
                return;

            throw;
        }

        Data = loaded;
    }

    private async Task<Dictionary<string, string?>> ReadStoreAsync()
    {
        var loaded = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in PathsToRead())
        {
            foreach (var (key, value) in await _store.ReadAsync(path))
            {
                loaded[ToConfigurationKey(key)] = value;
            }
        }

        return loaded;
    }

    private IEnumerable<string> PathsToRead()
    {
        if (!string.IsNullOrWhiteSpace(_options.SharedPath))
        {
            yield return _options.SharedPath;
        }

        // Read last so it wins: the service's own value overrides the shared one of the same name.
        if (!string.IsNullOrWhiteSpace(_options.ServicePath)
            && !string.Equals(_options.ServicePath, _options.SharedPath, StringComparison.OrdinalIgnoreCase))
        {
            yield return _options.ServicePath;
        }
    }

    /// <summary>
    /// A secret name uses a double underscore where a configuration path uses a colon, because a colon is
    /// not allowed in a secret name - the same convention environment variables follow.
    /// </summary>
    internal static string ToConfigurationKey(string secretName) =>
        secretName.Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
}

/// <summary>
/// The source that produces <see cref="SecretsConfigurationProvider"/>.
/// </summary>
public sealed class SecretsConfigurationSource : IConfigurationSource
{
    private readonly SecretStoreOptions _options;
    private readonly Func<ISecretStore> _storeFactory;

    public SecretsConfigurationSource(SecretStoreOptions options, Func<ISecretStore> storeFactory)
    {
        _options = options;
        _storeFactory = storeFactory;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new SecretsConfigurationProvider(_options, _storeFactory());
}
