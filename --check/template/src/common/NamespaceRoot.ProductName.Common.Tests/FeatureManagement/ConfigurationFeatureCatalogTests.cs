using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NamespaceRoot.ProductName.Common.Application.FeatureManagement;
using NamespaceRoot.ProductName.Common.FeatureManagement;
using NUnit.Framework;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.FeatureManagement;

/// <summary>
/// The catalogue decides what every service believes about a feature. What is pinned here is the part
/// that fails quietly if it breaks: a value cached instead of re-read, an environment override
/// ignored, or an unknown key answering true.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ConfigurationFeatureCatalogTests
{
    private const string Key = "sample.feature";

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (IConfigurationRoot Configuration, ConfigurationFeatureCatalog Catalog) Build(
        string environment = "Development",
        DateTimeOffset? now = null,
        params (string Path, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Path, v.Value)))
            .Build();

        var catalog = new ConfigurationFeatureCatalog(
            configuration,
            new Environment(environment),
            new Clock(now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        return (configuration, catalog);
    }

    [Test(Description = "Reads the declared value")]
    public void Should_ReadDeclaredValue()
    {
        var (_, catalog) = Build(values: ($"Features:{Key}:enabled", "true"));

        catalog.IsEnabled(Key).ShouldBeTrue();
    }

    [Test(Description = "A key nobody declared is off, not an error")]
    public void Should_AnswerFalse_When_KeyIsUnknown()
    {
        var (_, catalog) = Build(values: ($"Features:{Key}:enabled", "true"));

        catalog.IsEnabled("no.such.feature").ShouldBeFalse();
        catalog.Find("no.such.feature").ShouldBeNull();
    }

    [Test(Description = "Keys match regardless of case, and the declared spelling comes back")]
    public void Should_MatchKeysCaseInsensitively()
    {
        var (_, catalog) = Build(values: ($"Features:{Key}:enabled", "true"));

        catalog.IsEnabled("Sample.Feature").ShouldBeTrue();
        catalog.Find("SAMPLE.FEATURE")!.Key.ShouldBe(Key);
    }

    /// <summary>
    /// The reason one shared file can serve every stand. Without it a single file would force the same
    /// answer everywhere, which is the opposite of what a flag is for.
    /// </summary>
    [Test(Description = "An environment entry overrides the default for that environment only")]
    public void Should_PreferTheEnvironmentEntry()
    {
        var (_, development) = Build(
            "Development",
            values:
            [
                ($"Features:{Key}:enabled", "false"),
                ($"Features:{Key}:environments:Development", "true"),
            ]);

        var (_, production) = Build(
            "Production",
            values:
            [
                ($"Features:{Key}:enabled", "false"),
                ($"Features:{Key}:environments:Development", "true"),
            ]);

        development.IsEnabled(Key).ShouldBeTrue();
        production.IsEnabled(Key).ShouldBeFalse();
    }

    [Test(Description = "Environment names match regardless of case")]
    public void Should_MatchEnvironmentNamesCaseInsensitively()
    {
        var (_, catalog) = Build(
            "DEVELOPMENT",
            values:
            [
                ($"Features:{Key}:enabled", "false"),
                ($"Features:{Key}:environments:Development", "true"),
            ]);

        catalog.IsEnabled(Key).ShouldBeTrue();
    }

    /// <summary>
    /// The property the whole design rests on: a value changed underneath a live catalogue is seen on
    /// the next call. If this fails, flipping a flag needs a redeploy and the mechanism is pointless.
    /// </summary>
    [Test(Description = "A value changed after construction is picked up without rebuilding anything")]
    public void Should_FollowConfiguration_When_ItChanges()
    {
        var (configuration, catalog) = Build(values: ($"Features:{Key}:enabled", "false"));
        catalog.IsEnabled(Key).ShouldBeFalse();

        configuration[$"Features:{Key}:enabled"] = "true";

        catalog.IsEnabled(Key).ShouldBeTrue();
    }

    [Test(Description = "Carries the metadata a reader needs")]
    public void Should_CarryMetadata()
    {
        var (_, catalog) = Build(values:
        [
            ($"Features:{Key}:enabled", "true"),
            ($"Features:{Key}:effect", "Disables"),
            ($"Features:{Key}:description", "what it does"),
            ($"Features:{Key}:owner", "someone"),
            ($"Features:{Key}:ticket", "ABC-1"),
            ($"Features:{Key}:tags:0", "migration"),
        ]);

        var state = catalog.Find(Key).ShouldNotBeNull();
        state.Descriptor.Effect.ShouldBe(FeatureEffect.Disables);
        state.Descriptor.Description.ShouldBe("what it does");
        state.Descriptor.Owner.ShouldBe("someone");
        state.Descriptor.Ticket.ShouldBe("ABC-1");
        state.Descriptor.Tags.ShouldHaveSingleItem().ShouldBe("migration");
    }

    /// <summary>
    /// Expiry is what keeps a temporary flag from becoming permanent by inattention: once the date
    /// passes it is reported as debt rather than quietly continuing to work.
    /// </summary>
    [Test(Description = "A flag past its date is reported as expired, and still evaluates")]
    public void Should_ReportExpiry()
    {
        var (_, catalog) = Build(
            now: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            values:
            [
                ($"Features:{Key}:enabled", "true"),
                ($"Features:{Key}:expiresAt", "2026-01-31"),
            ]);

        var state = catalog.Find(Key).ShouldNotBeNull();
        state.Expired.ShouldBeTrue();
        state.Enabled.ShouldBeTrue("an expired flag is debt, not something that silently stops working");
    }

    [Test(Description = "A flag with no date is never expired")]
    public void Should_NotExpire_When_NoDateGiven()
    {
        var (_, catalog) = Build(values: ($"Features:{Key}:enabled", "true"));

        catalog.Find(Key)!.Expired.ShouldBeFalse();
    }

    [Test(Description = "Lists every declared feature, ordered by key")]
    public void Should_ListEverythingOrdered()
    {
        var (_, catalog) = Build(values:
        [
            ("Features:zulu:enabled", "true"),
            ("Features:alpha:enabled", "false"),
        ]);

        catalog.GetAll().Select(state => state.Key).ShouldBe(["alpha", "zulu"]);
    }
}
