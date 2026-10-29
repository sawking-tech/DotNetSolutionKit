using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NamespaceRoot.ProductName.Common.FeatureManagement;

namespace NamespaceRoot.ProductName.Common.Application.FeatureManagement;

/// <summary>
/// Reads the platform's features out of configuration on every call.
/// </summary>
/// <remarks>
/// <para>
/// Reading each time is what makes a flip apply to a running service: the configuration root hands
/// back whatever its providers hold now, and a file registered with <c>reloadOnChange</c> updates
/// them when it changes on disk. Caching a value here would quietly undo that.
/// </para>
/// <para>
/// Configuration is layered, and the layers are the point: the shared feature file underneath, then
/// an external store where one is wired up, then environment variables. The winning layer is
/// reported on each state so a management UI can explain why a value is what it is — an operator
/// switching a flag off and seeing nothing happen, because an environment variable sits on top, is
/// otherwise unexplainable from the outside.
/// </para>
/// <para>
/// Keys are matched without regard to case, in both directions: the same flag asked for as
/// <c>Provider.V2</c> or <c>provider.v2</c> is one flag. What comes back, though, is always the key
/// as declared — a UI should show the canonical spelling, not whatever the caller typed.
/// </para>
/// </remarks>
public sealed class ConfigurationFeatureCatalog : IFeatureCatalog
{
    /// <summary>Configuration section the feature file and every override live under.</summary>
    public const string SectionName = "Features";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public ConfigurationFeatureCatalog(
        IConfiguration configuration,
        IHostEnvironment environment,
        TimeProvider timeProvider)
    {
        _configuration = configuration;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public bool IsEnabled(string key) => Find(key)?.Enabled ?? false;

    /// <inheritdoc/>
    public IReadOnlyList<FeatureState> GetAll() =>
        _configuration.GetSection(SectionName)
            .GetChildren()
            .Select(Read)
            .OrderBy(state => state.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <inheritdoc/>
    public FeatureState? Find(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        // Deliberately not GetSection(key): that answers with a section whose Key is the spelling the
        // caller used, so asking for SAMPLE.FEATURE would echo SAMPLE.FEATURE back and a UI would show
        // whatever someone happened to type. Matching among the declared children keeps the canonical
        // spelling, while the match itself stays case-insensitive.
        var section = _configuration.GetSection(SectionName)
            .GetChildren()
            .FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase));

        return section is null ? null : Read(section);
    }

    private FeatureState Read(IConfigurationSection section)
    {
        var descriptor = new FeatureDescriptor
        {
            Key = section.Key,
            Enabled = section.GetValue("enabled", false),
            Effect = section.GetValue("effect", FeatureEffect.Enables),
            Environments = section.GetSection("environments")
                .GetChildren()
                .ToDictionary(child => child.Key, child => child.Get<bool>(), StringComparer.OrdinalIgnoreCase),
            Tags = section.GetSection("tags").Get<string[]>() ?? [],
            Description = section.GetValue<string?>("description"),
            Owner = section.GetValue<string?>("owner"),
            ExpiresAt = section.GetValue<DateOnly?>("expiresAt"),
            Ticket = section.GetValue<string?>("ticket"),
        };

        // A pinned flag takes its value from the shared file alone, whatever the layers above it say.
        var file = FeatureFile();
        if (file is not null && IsPinned(file, section.Path))
        {
            descriptor = descriptor with
            {
                Pinned = true,
                Enabled = file.TryGet($"{section.Path}:enabled", out var value) && bool.TryParse(value, out var on) && on,
                Environments = FileEnvironments(file, section.Path),
            };
        }

        var enabled = descriptor.ValueIn(_environment.EnvironmentName);
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        return new FeatureState
        {
            Descriptor = descriptor,
            Enabled = enabled,
            Source = descriptor.Pinned ? FeatureValueSource.Pinned : SourceOf(section, descriptor),
            Expired = descriptor.IsExpired(today),
        };
    }

    /// <summary>
    /// Which layer decided the value. Answered from the configuration provider that supplied the
    /// winning entry rather than guessed, so it stays right when the layering changes.
    /// </summary>
    private FeatureValueSource SourceOf(IConfigurationSection section, FeatureDescriptor descriptor)
    {
        var environmentPath = $"{section.Path}:environments:{_environment.EnvironmentName}";

        if (descriptor.Environments.ContainsKey(_environment.EnvironmentName))
            return ProviderFor(environmentPath) ?? FeatureValueSource.Environment;

        return ProviderFor($"{section.Path}:enabled") ?? FeatureValueSource.Default;
    }

    /// <summary>The provider of the shared feature file, found by its file name among the layers.</summary>
    private IConfigurationProvider? FeatureFile() =>
        (_configuration as IConfigurationRoot)?.Providers
            .OfType<FileConfigurationProvider>()
            .LastOrDefault(provider => string.Equals(
                Path.GetFileName(provider.Source.Path),
                FeatureConfigurationExtensions.FileName,
                StringComparison.OrdinalIgnoreCase));

    private static bool IsPinned(IConfigurationProvider file, string sectionPath) =>
        file.TryGet($"{sectionPath}:pinned", out var value) && bool.TryParse(value, out var pinned) && pinned;

    private static IReadOnlyDictionary<string, bool> FileEnvironments(IConfigurationProvider file, string sectionPath)
    {
        var path = $"{sectionPath}:environments";
        return file.GetChildKeys([], path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(environment => file.TryGet($"{path}:{environment}", out _))
            .ToDictionary(
                environment => environment,
                environment => file.TryGet($"{path}:{environment}", out var value) && bool.TryParse(value, out var on) && on,
                StringComparer.OrdinalIgnoreCase);
    }

    private FeatureValueSource? ProviderFor(string path)
    {
        if (_configuration is not IConfigurationRoot root)
            return null;

        // Later providers win in IConfiguration, so the last one holding the key is the one that
        // decided it.
        for (var i = root.Providers.Count() - 1; i >= 0; i--)
        {
            var provider = root.Providers.ElementAt(i);
            if (!provider.TryGet(path, out _))
                continue;

            var name = provider.GetType().Name;

            return name switch
            {
                _ when name.Contains("EnvironmentVariables", StringComparison.Ordinal) =>
                    FeatureValueSource.EnvironmentVariable,
                _ when name.Contains("Json", StringComparison.Ordinal) => FeatureValueSource.File,
                _ => FeatureValueSource.Store,
            };
        }

        return null;
    }
}
