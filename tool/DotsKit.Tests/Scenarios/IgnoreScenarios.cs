namespace DotsKit.Tests.Scenarios;

/// <summary>
/// <c>.dotskitignore</c> on a solution of a release before 3.0 brought to the template's sources: the upgrade leaves the
/// paths the file holds, lets the rest of the template in - the move of <c>Common</c> and the capabilities among it -
/// and brings a pin it spent back to the review. The tool runs as a person runs it, against a release from nuget.org.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class IgnoreScenarios
{
    private const string Release = "2.9.1";
    private const string Program = "src/services/ST.Demo.Orders/ST.Demo.Orders.API/Program.cs";
    private const string RulesFile = ".dotskitignore";
    private const string TeamsText = "// the team's own line, never taken back";

    /// <summary>A solution made by the release with the template alone, described by init, committed, with the team's mark in a file.</summary>
    private static async Task<Sandbox> SolutionOfTheReleaseAsync()
    {
        var solution = await Sandbox.EmptyAsync();
        var hive = Io.Folders.NewTemp("hive-" + Release);
        (await solution.DotnetAsync("new", "install", $"SawKing.DotNetSolutionKit::{Release}", "--debug:custom-hive", hive)).Code.ShouldBe(0);
        var made = await solution.DotnetAsync("new", "DotNetSolutionKit", "-N", "ST", "-P", "Demo", "-S", "Orders", "--Solution",
            "--allow-scripts", "yes", "--debug:custom-hive", hive);
        made.Code.ShouldBe(0, made.Output + made.Error);
        await solution.CommitAsync("made by the template " + Release);
        var init = await solution.DotskitAsync("init", "--template-version", Release, "--yes");
        init.Code.ShouldBe(0, init.Output + init.Error);
        solution.Write(Program, TeamsText + "\n" + solution.Read(Program));
        await solution.CommitAsync("the team's line");
        return solution;
    }

    private static async Task<(Sandbox Solution, Io.ProcessResult Upgraded)> UpgradedWithAsync(string rules)
    {
        var solution = await SolutionOfTheReleaseAsync();
        solution.Write(RulesFile, rules.ReplaceLineEndings("\n"));
        await solution.CommitAsync("the rules");
        var upgraded = await solution.DotskitAsync("upgrade", "--yes", "--all-at-once");
        return (solution, upgraded);
    }

    [Test]
    public async Task AHeldFile_KeepsTheTeamsText_WhileTheRestOfTheTemplateComesIn()
    {
        // A held file is the team's: the upgrade writes none of it, so a file of 2.9 in a solution of 3.0 may not build
        // until the team brings it up. That is the price of the hold, and it is what the scenario shows.
        var (solution, upgraded) = await UpgradedWithAsync(Program + "\n");

        upgraded.Output.ShouldContain($"ignored   {Program}");
        solution.Read(Program).StartsWith(TeamsText, StringComparison.Ordinal).ShouldBeTrue("the team's line stands");
        solution.Exists("src/capabilities").ShouldBeTrue("the template's own part of 3.0 is merged");
        upgraded.Output.ShouldContain("the build fails, by project", customMessage: "a held file of an older release is the team's to bring up");
    }

    [Test]
    public async Task AHeldFolder_TheTemplateWritesNothingInIt()
    {
        var (solution, upgraded) = await UpgradedWithAsync("src/common/ST.Demo.Common/\n");

        upgraded.Output.ShouldContain("ignored   src/common/ST.Demo.Common/");
        solution.Exists("src/common/ST.Demo.Common/ST.Demo.Common.csproj").ShouldBeTrue("the folder is held, not removed");
    }

    [Test]
    public async Task AHeldFileTheTemplateOnlyMoves_LeavesTheBuildGreen_AndTheFileStands()
    {
        // CLAUDE.md is the template's: 3.0 changes it, and a solution builds with either text. A held rule keeps it.
        var (solution, upgraded) = await UpgradedWithAsync("CLAUDE.md\n");
        var held = solution.Read("CLAUDE.md");

        upgraded.Code.ShouldBe(0, upgraded.Output + upgraded.Error);
        upgraded.Output.ShouldContain("ignored   CLAUDE.md");
        held.ShouldNotContain("load-context", customMessage: "the file the upgrade would bring is held back");
    }

    [Test]
    public async Task APinTheUpgradeSpends_ComesToTheReview_AndOneStillHolding_Stays()
    {
        var (solution, upgraded) = await UpgradedWithAsync($"CLAUDE.md@2.9\nREADME.md@major\n");

        // 2.9 holds only within 2.9.x: the upgrade to the sources passes it, and the review brings the line back.
        var rules = solution.Read(RulesFile);
        rules.ShouldContain("# CLAUDE.md@3.", customMessage: "the pin of 2.9 is spent and commented out at the line of the target");
        rules.ShouldContain("README.md@major", customMessage: "@major holds for good and is left as the team wrote it");
        upgraded.Output.ShouldContain("pin(s) the upgrade spent");
    }

    [Test]
    public async Task NoRulesFile_IsNoObstacle_AndTheToolWritesNone()
    {
        var solution = await SolutionOfTheReleaseAsync();

        var upgraded = await solution.DotskitAsync("upgrade", "--yes", "--all-at-once");

        upgraded.Code.ShouldBe(0, upgraded.Output + upgraded.Error);
        upgraded.Output.ShouldNotContain("ignored   ");
        solution.Exists(RulesFile).ShouldBeFalse("the tool writes no rules file of its own");
    }
}
