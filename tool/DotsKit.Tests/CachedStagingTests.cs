using DotsKit.Generation;
using DotsKit.Io;

namespace DotsKit.Tests;

/// <summary>The template's output generated once per manifest and handed out as a copy of its own.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class CachedStagingTests
{
    private const string File1 = "src/Directory.Build.props";

    /// <summary>Counts the generations and writes the manifest's version into its output.</summary>
    private sealed class Counting : IStaging
    {
        public int Calls { get; private set; }

        public Task<string> BuildAsync(Manifest manifest, bool withSolutionFile, CancellationToken ct)
        {
            Calls++;
            var root = Path.Combine(Folders.NewTemp("counting"), "solution");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, File1), manifest.Template);
            return Task.FromResult(root);
        }
    }

    /// <summary>A manifest no other test shares, so the cache in the temp folder starts empty for it.</summary>
    private static Manifest Unique(string template) =>
        new() { Template = template, Solution = ["--NamespaceRoot", "Cache" + Guid.NewGuid().ToString("N")], Services = [["--ServiceNameOrCustom", "Orders"]] };

    [Test]
    public async Task TheSameManifest_IsGeneratedOnce_AndEachCallGetsACopyOfItsOwn()
    {
        var inner = new Counting();
        var staging = new CachedStaging(inner, "2.9.0", () => null);
        var manifest = Unique("2.7.0");

        var first = await staging.BuildAsync(manifest, false, CancellationToken.None);
        File.WriteAllText(Path.Combine(first, File1), "changed by a plan");
        var second = await staging.BuildAsync(manifest with { Upgrade = new UpgradeInProgress { To = "2.9.0", Done = ["shared"] } }, false, CancellationToken.None);

        inner.Calls.ShouldBe(1);
        second.ShouldNotBe(first);
        File.ReadAllText(Path.Combine(second, File1)).ShouldBe("2.7.0");
    }

    [Test]
    public async Task AnotherVersion_AnotherSolutionFile_OrChangedSources_AreGeneratedAgain()
    {
        var inner = new Counting();
        var sources = Folders.NewTemp("sources");
        File.WriteAllText(Path.Combine(sources, "template.json"), "{}");
        var staging = new CachedStaging(inner, "2.9.0", () => sources);
        var manifest = Unique(TemplateSource.SourcesVersion);

        await staging.BuildAsync(manifest, false, CancellationToken.None);
        await staging.BuildAsync(manifest, true, CancellationToken.None);
        await staging.BuildAsync(manifest with { Template = "2.7.0" }, false, CancellationToken.None);
        File.WriteAllText(Path.Combine(sources, "new.cs"), "");
        await staging.BuildAsync(manifest, false, CancellationToken.None);
        // A rename keeps the count and the newest write: the stamp still tells it.
        File.Move(Path.Combine(sources, "new.cs"), Path.Combine(sources, "renamed.cs"));
        await staging.BuildAsync(manifest, false, CancellationToken.None);

        inner.Calls.ShouldBe(5);
    }
}
