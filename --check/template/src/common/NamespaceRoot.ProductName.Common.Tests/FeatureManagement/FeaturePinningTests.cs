using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Common.Application.FeatureManagement;
using NamespaceRoot.ProductName.Common.FeatureManagement;

namespace NamespaceRoot.ProductName.Common.Tests.FeatureManagement;

/// <summary>
/// A flag pinned in the shared file takes its value from the file, over the store and environment
/// variables: the way to change one flag now while the store cannot be reached, without waiting for it.
/// </summary>
[TestFixture]
public class FeaturePinningTests
{
    private const string Key = "payments.new-provider";
    private string _folder = null!;

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("features-").FullName;

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    /// <summary>
    /// The layers a service has: the shared file, a store above it, environment variables on top. The
    /// store is an in-memory provider, which the catalogue reports as a store like any other.
    /// </summary>
    private ConfigurationFeatureCatalog Catalog(
        string file,
        (string Path, string Value)[]? store = null,
        (string Name, string Value)[]? variables = null,
        string environment = "Production")
    {
        File.WriteAllText(Path.Combine(_folder, FeatureConfigurationExtensions.FileName), file);

        var prefix = $"PINNING_{Guid.NewGuid():N}_";
        foreach (var (name, value) in variables ?? [])
            System.Environment.SetEnvironmentVariable(prefix + name, value);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(new PhysicalFileProvider(_folder), FeatureConfigurationExtensions.FileName, optional: false, reloadOnChange: false)
            .AddInMemoryCollection((store ?? []).Select(v => new KeyValuePair<string, string?>(v.Path, v.Value)))
            .AddEnvironmentVariables(prefix)
            .Build();

        return new ConfigurationFeatureCatalog(configuration, new Environment(environment), TimeProvider.System);
    }

    private static string FeatureFile(bool enabled, bool pinned, string environments = "") => $$"""
        { "Features": { "{{Key}}": { "enabled": {{enabled.ToString().ToLowerInvariant()}}, "pinned": {{pinned.ToString().ToLowerInvariant()}}{{environments}} } } }
        """;

    [Test]
    public void A_pinned_flag_takes_the_file_value_over_the_store()
    {
        var catalog = Catalog(FeatureFile(enabled: false, pinned: true), store: [($"Features:{Key}:enabled", "true")]);

        var state = catalog.Find(Key)!;
        state.Enabled.ShouldBeFalse("the store says on, the pin says the file decides");
        state.Source.ShouldBe(FeatureValueSource.Pinned);
        state.Descriptor.Pinned.ShouldBeTrue();
    }

    [Test]
    public void A_pinned_flag_takes_the_file_value_over_an_environment_variable()
    {
        var catalog = Catalog(
            FeatureFile(enabled: false, pinned: true, environments: ", \"environments\": { \"Production\": true }"),
            variables: [($"Features__{Key}__environments__Production", "false")]);

        var state = catalog.Find(Key)!;
        state.Enabled.ShouldBeTrue("the file's entry for this environment, not the variable");
        state.Source.ShouldBe(FeatureValueSource.Pinned);
    }

    [Test]
    public void Without_a_pin_the_store_still_wins()
    {
        var catalog = Catalog(FeatureFile(enabled: false, pinned: false), store: [($"Features:{Key}:enabled", "true")]);

        var state = catalog.Find(Key)!;
        state.Enabled.ShouldBeTrue();
        state.Source.ShouldBe(FeatureValueSource.Store);
    }

    [Test]
    public void A_pin_set_anywhere_but_the_file_pins_nothing()
    {
        var catalog = Catalog(
            FeatureFile(enabled: false, pinned: false),
            store: [($"Features:{Key}:pinned", "true"), ($"Features:{Key}:enabled", "true")]);

        var state = catalog.Find(Key)!;
        state.Source.ShouldBe(FeatureValueSource.Store, "only the file can pin: the store must not be able to lock itself in");
        state.Enabled.ShouldBeTrue();
    }

    [Test]
    public async Task Every_start_names_the_pinned_flags()
    {
        var catalog = Catalog(FeatureFile(enabled: true, pinned: true));
        var log = new RecordingLogger();

        await new PinnedFeaturesAnnouncer(catalog, log).StartAsync(CancellationToken.None);

        log.Warnings.ShouldHaveSingleItem().ShouldContain($"{Key}=True",
            customMessage: "a forgotten pin would otherwise keep overriding the store unnoticed");
    }

    [Test]
    public async Task A_start_without_pins_warns_about_nothing()
    {
        var log = new RecordingLogger();

        await new PinnedFeaturesAnnouncer(Catalog(FeatureFile(enabled: true, pinned: false)), log).StartAsync(CancellationToken.None);

        log.Warnings.ShouldBeEmpty();
    }

    private sealed class RecordingLogger : ILogger<PinnedFeaturesAnnouncer>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
