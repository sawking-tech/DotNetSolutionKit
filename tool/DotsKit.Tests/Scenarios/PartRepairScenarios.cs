using DotsKit.Commands;
using DotsKit.Generation;
using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Tests.Scenarios;

/// <summary>
/// An upgrade over a type of Common the template moved to another namespace: a part at a time, the part of Common
/// repairs the usings of every project of the solution, the team's code and the service's files of the template
/// alike, and the service's own part later keeps that repair rather than writing over it; all at once, the one run
/// repairs them. The real
/// planner, writer, git and repair run; the template's outputs are folders the test writes.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class PartRepairScenarios
{
    private const string From = "2.7.0";
    private const string To = "2.9.0";
    private const string OldNamespace = "ST.Demo.Common.Events";
    private const string NewNamespace = "ST.Demo.Common.Domain.Events";
    private const string Solution = "src/services/ST.Demo.All.sln";
    private const string CommonProject = "src/common/ST.Demo.Common/ST.Demo.Common.csproj";
    private const string OldEvent = "src/common/ST.Demo.Common/Events/DomainEvent.cs";
    private const string NewEvent = "src/common/ST.Demo.Common/Domain/Events/DomainEvent.cs";
    private const string OrdersProject = "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/ST.Demo.Orders.Domain.csproj";
    private const string TemplatesOrder = "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Order.cs";
    private const string TeamsRefund = "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Refund.cs";

    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";
    private const string TeamProject = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>\n  </PropertyGroup>\n</Project>\n";
    private const string NextProject = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n    <LangVersion>latest</LangVersion>\n  </PropertyGroup>\n</Project>\n";
    private const string EmptySolution = "Microsoft Visual Studio Solution File, Format Version 12.00\nGlobal\nEndGlobal\n";
    private const string EventOf = "namespace {0};\n\npublic sealed class DomainEvent;\n";
    private const string OrderOf = "using {0};\n\nnamespace ST.Demo.Orders.Domain;\n\npublic sealed class Order\n{{\n    public DomainEvent? {1} {{ get; set; }}\n}}\n";
    private const string Property = "Last";
    private const string OtherUsing = "using System.Text;\n";
    private const string DomainProject = "src/common/ST.Demo.Common.Domain/ST.Demo.Common.Domain.csproj";
    private const string MovedEvent = "src/common/ST.Demo.Common.Domain/Events/DomainEvent.cs";
    private const string OrdersReferencingDomain =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n"
        + "    <ProjectReference Include=\"..\\..\\..\\common\\ST.Demo.Common.Domain\\ST.Demo.Common.Domain.csproj\" />\n  </ItemGroup>\n</Project>\n";
    private const string RefundOf = "using {0};\n\nnamespace ST.Demo.Orders.Domain;\n\npublic sealed class Refund\n{{\n    public DomainEvent? Cause {{ get; set; }}\n}}\n";

    private static readonly ProcessRunner Runner = new();

    [TestCase(false)]
    [TestCase(true)]
    public async Task ThePartOfCommon_RepairsTheUsingsOfTheService_AndTheServicesPartKeepsThem(bool allAtOnce)
    {
        var root = await SolutionAsync();
        var lines = new List<string>();
        var parts = new List<ExitCode>();
        string[] args = allAtOnce ? ["--yes", "--all-at-once"] : ["--yes"];

        // shared, common/ST.Demo.Common, capabilities, then ST.Demo.Orders, or every part at once.
        parts.Add(await Upgrade(lines).RunAsync(Options.Parse("upgrade", args), root, CancellationToken.None));

        parts.ShouldAllBe(code => code == ExitCode.Done);
        // shared, common/ST.Demo.Common, then ST.Demo.Orders; a part at a time the run commits each part before the next,
        // all at once leaves the one merge for the team to commit.
        (await Runner.RunCheckedAsync("git", ["log", "--format=%s"], root, CancellationToken.None)).Split('\n')
            .Count(s => s.StartsWith("dotskit: the upgrade to", StringComparison.Ordinal)).ShouldBe(allAtOnce ? 0 : 3);
        // By git, the tip each part leaves on dotskit/template is the base of the next: the team's branch gets one adoption, the first.
        (await Runner.RunCheckedAsync("git", ["log", "--format=%s"], root, CancellationToken.None)).Split('\n')
            .Count(s => s.StartsWith("dotskit: the solution is of the template", StringComparison.Ordinal)).ShouldBe(1);
        Exists(root, OldEvent).ShouldBeFalse();
        Manifest.Load(root)!.Template.ShouldBe(To);
        Read(root, TemplatesOrder).ShouldBe(string.Format(OrderOf, NewNamespace, Property));
        Read(root, TeamsRefund).ShouldBe(string.Format(RefundOf, NewNamespace));
        // The merge brings the template's Order.cs and project to the new namespace - all at once as the template's change, a
        // part at a time as the repair the step's two sides carry - and the repair takes the team's file; all at once it adds
        // the reference the template's project has not got.
        lines.ShouldContain($"  ST.Demo.Orders.Domain: using {OldNamespace} -> {NewNamespace}{(allAtOnce ? "" : " (2 files)")}; reference to ST.Demo.Common");
    }

    [Test]
    public async Task APartInConflict_IsNoPartOfDone_UntilItsMergeIsCommitted()
    {
        var root = await SolutionAsync();
        // The team set warnings as errors in the project of Common, and the next template edits it too: the part of
        // Common, not the last, conflicts.
        Put(root, CommonProject, TeamProject);
        await CommitAsync(root, "the team's project");
        var lines = new List<string>();
        var outputs = new Outputs(NextChangesCommonProject: true);

        // The run commits the shared part and stops at the part of Common in conflict.
        var stopped = await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--yes"]), root, CancellationToken.None);

        stopped.ShouldBe(ExitCode.Conflicts);
        // The stopped run names only the parts whose merge is committed: the conflicted part waits outside Done.
        var open = Manifest.Load(root)!;
        open.Upgrade!.Done.ShouldBe([UpgradeParts.Shared]);
        open.Upgrade.Parts.ShouldBe([UpgradeParts.Shared, "common/ST.Demo.Common", "ST.Demo.Orders"]);
        lines.ShouldContain(l => l.Contains("Conflicts in common/ST.Demo.Common: 2 of 3 part(s) of the upgrade to 2.9.0 left", StringComparison.Ordinal));

        Put(root, CommonProject, NextProject);
        await CommitAsync(root, "the conflict resolved");
        var finished = await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--continue"]), root, CancellationToken.None);

        finished.ShouldBe(ExitCode.Done);
        Manifest.Load(root)!.Template.ShouldBe(To);
        Read(root, CommonProject).ShouldBe(NextProject);
    }

    [Test]
    public async Task TheLastPartLeftInConflict_KeepsTheUpgradeOpen_AndTheRunAfterTheResolutionFinishesIt()
    {
        var root = await SolutionAsync();
        // The team renamed the property the next template renames too: the service's part, the last, conflicts.
        Put(root, TemplatesOrder, string.Format(OrderOf, OldNamespace, "Latest"));
        await CommitAsync(root, "the team's name");
        var lines = new List<string>();
        var outputs = new Outputs(NextProperty: "Previous");

        // One run commits the shared parts and the part of Common and stops at the service's part, the last, in conflict.
        var stopped = await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--yes"]), root, CancellationToken.None);

        var open = Manifest.Load(root)!;
        Put(root, TemplatesOrder, string.Format(OrderOf, NewNamespace, "Previous"));
        await CommitAsync(root, "the conflict resolved");
        var finished = await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--continue"]), root, CancellationToken.None);

        stopped.ShouldBe(ExitCode.Conflicts);
        open.Template.ShouldBe(From);
        // The conflicted part counts as merged only once its merge is committed: Done names the parts before it.
        open.Upgrade!.Done.ShouldBe([UpgradeParts.Shared, "common/ST.Demo.Common"]);
        lines.ShouldContain(l => l.Contains("then dotskit upgrade --continue repairs the usings, builds and finishes the upgrade", StringComparison.Ordinal));
        finished.ShouldBe(ExitCode.Done);
        Manifest.Load(root)!.Template.ShouldBe(To);
        Manifest.Load(root)!.Upgrade.ShouldBeNull();
        Read(root, TeamsRefund).ShouldBe(string.Format(RefundOf, NewNamespace));
        // The run after the resolution takes the part again: its merge stands, so the run commits it and the upgrade is over.
        lines.ShouldContain(l => l.EndsWith(": ST.Demo.Orders, 3 of 3, the last.", StringComparison.Ordinal));
        (await Runner.RunCheckedAsync("git", ["log", "--format=%s"], root, CancellationToken.None)).Split('\n')
            .ShouldContain("dotskit: the upgrade to 2.9.0, finished");
    }

    [Test]
    public async Task AServiceFileTheTemplateChangesBeyondTheUsing_MergesCleanAfterThePartOfCommonRepairedIt()
    {
        var root = await SolutionAsync();
        var lines = new List<string>();
        var outputs = new Outputs(NextAddsAUsing: true);
        (await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--yes"]), root, CancellationToken.None)).ShouldBe(ExitCode.Done);
        Manifest.Load(root)!.Template.ShouldBe(To);
        Read(root, TemplatesOrder).ShouldBe(OtherUsing + string.Format(OrderOf, NewNamespace, Property));
    }

    [Test]
    public async Task AProjectTheTemplateAddsToCommon_IsAPartOfItsOwn_AndTheTypeMovedThereIsFollowed()
    {
        var root = await SolutionAsync();
        var lines = new List<string>();
        var outputs = new Outputs(NextMovesToANewProject: true);
        // shared, the new common/ST.Demo.Common.Domain the event goes to, then common/ST.Demo.Common it leaves, capabilities, ST.Demo.Orders.
        (await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--yes"]), root, CancellationToken.None)).ShouldBe(ExitCode.Done);
        lines.ShouldContain(l => l.EndsWith(": shared, 1 of 4.", StringComparison.Ordinal), "the count takes the project the template adds");
        Exists(root, MovedEvent).ShouldBeTrue();
        Read(root, TeamsRefund).Contains(NewNamespace, StringComparison.Ordinal).ShouldBeTrue();
        Manifest.Load(root)!.Template.ShouldBe(To);
        Exists(root, OldEvent).ShouldBeFalse();
        Read(root, DomainProject).ShouldBe(Project);
        Read(root, TeamsRefund).ShouldBe(string.Format(RefundOf, NewNamespace));
        Read(root, OrdersProject).Split("ST.Demo.Common.Domain.csproj").Length.ShouldBe(2, "one reference to the new project, not two");
    }

    [Test]
    public async Task AFileTheTemplateMovesToAnotherProjectOfCommon_TakesTheTeamsChange_AcrossTheParts()
    {
        var root = await SolutionAsync();
        Put(root, RulesProject, Project);
        Put(root, RulesDomainProject, Project);
        Put(root, RulesInApplication, RulesOf(TeamsLine));
        await CommitAsync(root, "the team's rule");
        var lines = new List<string>();
        // shared, common/ST.Demo.Common, common/ST.Demo.Common.Domain the file goes to, common/ST.Demo.Common.Application it leaves, capabilities, ST.Demo.Orders.
        (await Upgrade(lines, new MovingRules()).RunAsync(Options.Parse("upgrade", ["--yes"]), root, CancellationToken.None)).ShouldBe(ExitCode.Done);
        Manifest.Load(root)!.Template.ShouldBe(To);
        Exists(root, RulesInApplication).ShouldBeFalse();
        Read(root, RulesInDomain).ShouldContain(TeamsLine);
        Read(root, RulesInDomain).ShouldContain(TemplatesLine);
    }

    private const string RulesProject = "src/common/ST.Demo.Common.Application/ST.Demo.Common.Application.csproj";
    private const string RulesDomainProject = "src/common/ST.Demo.Common.Domain/ST.Demo.Common.Domain.csproj";
    private const string RulesInApplication = "src/common/ST.Demo.Common.Application/Rules/Rules.cs";
    private const string RulesInDomain = "src/common/ST.Demo.Common.Domain/Rules/Rules.cs";
    private const string TeamsLine = "    // the team's own rule";
    private const string TemplatesLine = "    // the template's new rule";

    /// <summary>A file long enough for git to pair its old path with its new one, with a line of its own at the end.</summary>
    private static string RulesOf(string last, string first = "") =>
        "namespace ST.Demo.Common.Rules;\n\npublic static class Rules\n{\n" + (first.Length > 0 ? first + "\n" : "")
        + string.Concat(Enumerable.Range(1, 12).Select(i => $"    public const int Rule{i} = {i};\n")) + last + "\n}\n";

    /// <summary>2.7.0 keeps Rules.cs in Common.Application; 2.9.0 moves it to Common.Domain and adds a rule at its top.</summary>
    private sealed class MovingRules : IStaging
    {
        public Task<string> BuildAsync(Manifest manifest, bool withSolutionFile, CancellationToken ct)
        {
            var next = manifest.Template == To;
            var root = Folders.NewTemp("moving-rules-output");
            Put(root, CommonProject, Project);
            Put(root, OldEvent, string.Format(EventOf, OldNamespace));
            Put(root, OrdersProject, Project);
            Put(root, TemplatesOrder, string.Format(OrderOf, OldNamespace, Property));
            Put(root, RulesProject, Project);
            Put(root, RulesDomainProject, Project);
            Put(root, next ? RulesInDomain : RulesInApplication, next ? RulesOf("", TemplatesLine) : RulesOf(""));
            return Task.FromResult(root);
        }
    }

    /// <summary>The command as Program.cs puts it together, the template's outputs taken from <see cref="Outputs"/>.</summary>
    private static UpgradeCommand Upgrade(List<string> lines, IStaging? outputs = null)
    {
        var projects = new SolutionProjects(Runner);
        var terminal = new Lines(lines);
        var git = new Git(Runner);
        var builder = new Builds();
        var repair = new ReferenceRepair(new RepairWriter(), builder);
        var planner = new Planner(outputs ?? new Outputs(), projects, new GitRenames(Runner), new GitIgnoredFiles(Runner), repair);
        var writer = new PlanWriter(projects, new GitTemplateMerge(Runner, Sandbox.Self), repair);
        return new UpgradeCommand(planner, new PlanExecution(new PlanReport(terminal), writer, builder, repair, terminal, git), git, new GitIgnoredFiles(Runner), terminal, To, onSources: false);
    }

    /// <summary>A solution of the template 2.7.0 in git: Common with its event, the service's file of the template and the team's own.</summary>
    private static async Task<string> SolutionAsync()
    {
        var root = Folders.NewTemp("part-repair");
        Put(root, Solution, EmptySolution);
        Put(root, CommonProject, Project);
        Put(root, OldEvent, string.Format(EventOf, OldNamespace));
        Put(root, OrdersProject, Project);
        Put(root, TemplatesOrder, string.Format(OrderOf, OldNamespace, Property));
        Put(root, TeamsRefund, string.Format(RefundOf, OldNamespace));
        Put(root, Layout.ManifestPath, new Manifest { Template = From, Solution = ["--NamespaceRoot", "ST"], Services = [["--ServiceNameOrCustom", "Orders"]] }.Serialize());
        await Runner.RunCheckedAsync("git", ["init", "-q"], root, CancellationToken.None);
        await CommitAsync(root, "the solution");
        return root;
    }

    private static async Task CommitAsync(string root, string message)
    {
        await Runner.RunCheckedAsync("git", ["add", "-A"], root, CancellationToken.None);
        await Runner.RunCheckedAsync("git", ["-c", "core.hooksPath=/dev/null", "commit", "-q", "--allow-empty", "-m", message], root, CancellationToken.None);
    }

    private static void Put(string root, string path, string text)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static bool Exists(string root, string path) => File.Exists(Path.Combine(root, path));

    /// <summary>The template's outputs: 2.7.0 keeps the event in Common.Events, 2.9.0 in Common.Domain.Events.</summary>
    /// <param name="NextProperty">The name 2.9.0 gives the property of Order.</param>
    /// <param name="NextAddsAUsing">2.9.0 also adds a using of its own to Order.cs, beside the one of the moved event.</param>
    /// <param name="NextMovesToANewProject">2.9.0 moves the event to a project it adds to Common, and Orders references it.</param>
    /// <param name="NextChangesCommonProject">2.9.0 also edits the project of Common, beside the move of the event.</param>
    private sealed record Outputs(string NextProperty = Property, bool NextAddsAUsing = false, bool NextMovesToANewProject = false, bool NextChangesCommonProject = false) : IStaging
    {
        public Task<string> BuildAsync(Manifest manifest, bool withSolutionFile, CancellationToken ct)
        {
            var next = manifest.Template == To;
            var root = Folders.NewTemp("part-repair-output");
            Put(root, CommonProject, next && NextChangesCommonProject ? NextProject : Project);
            if (!next)
            {
                Put(root, OldEvent, string.Format(EventOf, OldNamespace));
            }
            else if (NextMovesToANewProject)
            {
                Put(root, DomainProject, Project);
                Put(root, MovedEvent, string.Format(EventOf, NewNamespace));
            }
            else
            {
                Put(root, NewEvent, string.Format(EventOf, NewNamespace));
            }

            Put(root, OrdersProject, next && NextMovesToANewProject ? OrdersReferencingDomain : Project);
            Put(root, TemplatesOrder, (next && NextAddsAUsing ? OtherUsing : "") + string.Format(OrderOf, next ? NewNamespace : OldNamespace, next ? NextProperty : Property));
            return Task.FromResult(root);
        }
    }

    private sealed class Builds : IBuilder
    {
        public Task<ProcessResult> BuildAsync(string root, CancellationToken ct) => Task.FromResult(new ProcessResult(0, "", ""));
    }

    private sealed class Lines(List<string> lines) : ITerminal
    {
        public void Info(string line) => lines.Add(line);

        public void Error(string line) => lines.Add(line);

        public bool Confirm(string question) => true;
    }
}

