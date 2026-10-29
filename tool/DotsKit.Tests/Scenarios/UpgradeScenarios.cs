using DotsKit.PackageUpdates;

namespace DotsKit.Tests.Scenarios;

/// <summary>
/// A solution of an earlier release brought to the template's sources by dotskit upgrade, with the team's changes
/// and its package pins kept, and then added to.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class UpgradeScenarios
{
    private const string Program = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.API/Program.cs";
    private const string TeamsLine = "// the team's own line";
    private const string Pinned = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string PinnedVersion = "8.0.10";

    /// <summary>Where a solution keeps its package versions: one file, or one per database since 2.7.</summary>
    private static readonly string[] VersionFiles = ["src/Directory.Packages.props", "src/package-versions/postgres.props"];

    /// <summary>
    /// A solution made by a release from nuget.org with the template alone, as before dotskit, and described by init:
    /// the first service with the solution, the second with flags of its own, which leave the root as it was.
    /// </summary>
    private static async Task<Sandbox> SolutionOfAsync(string version)
    {
        var solution = await Sandbox.EmptyAsync();
        var hive = Io.Folders.NewTemp("hive-" + version);
        (await solution.DotnetAsync("new", "install", $"SawKing.DotNetSolutionKit::{version}", "--debug:custom-hive", hive)).Code.ShouldBe(0);
        foreach (var args in new[] { new[] { "-S", "Orders", "-M", "false", "--Storage", "true" }, ["-S", "Billing", "--Storage", "true", "--Messaging", "outbox"] })
        {
            var made = await solution.DotnetAsync(["new", "DotNetSolutionKit", "-N", "Acme", "-P", "Shop", .. args, "--debug:custom-hive", hive]);
            made.Code.ShouldBe(0, made.Output + made.Error);
        }
        // Before 2.8 a service stayed out of All.sln until manual-add-projects.sh, which adds it as this does.
        await new Solutions.SolutionProjects(new Io.ProcessRunner()).AddMissingAsync(solution.Root, CancellationToken.None);
        await solution.CommitAsync($"made by the template {version}");
        // A solution says its version since 2.7.0; before, the command names it.
        var init = await solution.DotskitAsync("init", "--template-version", version, "--yes");
        init.Code.ShouldBe(0, init.Output + init.Error);
        init.Output.ShouldContain(" files of the template reproduced.");
        await solution.CommitAsync("described");
        return solution;
    }

    /// <summary>The first minor of the major on nuget.org, and the previous minor (CONTRIBUTING.md, Releases).</summary>
    [TestCase("2.6.1")]
    [TestCase("2.7.0")]
    public async Task ASolutionOfAnEarlierMinor_IsUpgraded_KeepsTheTeamsChangesAndPins_BuildsPassesItsTests_AndTakesANewService(string version)
    {
        var solution = await SolutionOfAsync(version);
        solution.Write(Program, solution.Read(Program) + "\n" + TeamsLine + "\n");
        var manifest = Manifest.Parse(solution.Read(Layout.ManifestPath));
        var pinned = manifest with { Packages = new PackagePolicy { Pinned = new() { [Pinned] = PinnedVersion } } };
        solution.Write(Layout.ManifestPath, pinned.Serialize());
        await solution.CommitAsync("the team's line and pin");

        var upgraded = await solution.DotskitAsync("upgrade", "--yes");

        upgraded.Code.ShouldBe(0, upgraded.Output + upgraded.Error);
        upgraded.Output.ShouldContain($"{Pinned}: pinned on {PinnedVersion}");
        solution.Read(Program).ShouldContain(TeamsLine);
        PackageVersions.Read(string.Concat(VersionFiles
            .Where(solution.Exists).Select(solution.Read)))[Pinned].ShouldBe(PinnedVersion);
        solution.Exists("src/services/manual-add-projects.sh").ShouldBeFalse();
        var tests = await solution.DotnetAsync("test", "src/services/Acme.Shop.All.sln", "--filter", "TestCategory!=Integration");
        tests.Code.ShouldBe(0, tests.Output + tests.Error);
        await solution.CommitAsync("upgraded");

        var added = await solution.DotskitAsync("new", "-S", "Catalog", "--yes");
        added.Code.ShouldBe(0, added.Output + added.Error);
    }
}
