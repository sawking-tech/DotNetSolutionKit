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
/// repairs them; in either merge. The real
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
    private const string OldNamespace = "Acme.Shop.Common.Events";
    private const string NewNamespace = "Acme.Shop.Common.Domain.Events";
    private const string Solution = "src/services/Acme.Shop.All.sln";
    private const string CommonProject = "src/common/Acme.Shop.Common/Acme.Shop.Common.csproj";
    private const string OldEvent = "src/common/Acme.Shop.Common/Events/DomainEvent.cs";
    private const string NewEvent = "src/common/Acme.Shop.Common/Domain/Events/DomainEvent.cs";
    private const string OrdersProject = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Acme.Shop.Orders.Domain.csproj";
    private const string TemplatesOrder = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Order.cs";
    private const string TeamsRefund = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Refund.cs";

    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";
    private const string EmptySolution = "Microsoft Visual Studio Solution File, Format Version 12.00\nGlobal\nEndGlobal\n";
    private const string EventOf = "namespace {0};\n\npublic sealed class DomainEvent;\n";
    private const string OrderOf = "using {0};\n\nnamespace Acme.Shop.Orders.Domain;\n\npublic sealed class Order\n{{\n    public DomainEvent? {1} {{ get; set; }}\n}}\n";
    private const string Property = "Last";
    private const string OtherUsing = "using System.Text;\n";
    private const string DomainProject = "src/common/Acme.Shop.Common.Domain/Acme.Shop.Common.Domain.csproj";
    private const string MovedEvent = "src/common/Acme.Shop.Common.Domain/Events/DomainEvent.cs";
    private const string OrdersReferencingDomain =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n"
        + "    <ProjectReference Include=\"..\\..\\..\\common\\Acme.Shop.Common.Domain\\Acme.Shop.Common.Domain.csproj\" />\n  </ItemGroup>\n</Project>\n";
    private const string RefundOf = "using {0};\n\nnamespace Acme.Shop.Orders.Domain;\n\npublic sealed class Refund\n{{\n    public DomainEvent? Cause {{ get; set; }}\n}}\n";

    private static readonly ProcessRunner Runner = new();

    [TestCase(MergeMode.Git, false)]
    [TestCase(MergeMode.Files, false)]
    [TestCase(MergeMode.Git, true)]
    [TestCase(MergeMode.Files, true)]
    public async Task ThePartOfCommon_RepairsTheUsingsOfTheService_AndTheServicesPartKeepsThem(string merge, bool allAtOnce)
    {
        var root = await SolutionAsync();
        var lines = new List<string>();
        var parts = new List<ExitCode>();
        var repaired = new List<bool>();
        string[] args = allAtOnce ? ["--yes", "--merge", merge, "--all-at-once"] : ["--yes", "--merge", merge];

        // shared, common/Acme.Shop.Common, capabilities, then Acme.Shop.Orders, or every part at once; the team commits each run.
        for (var run = 0; run < (allAtOnce ? 1 : 4); run++)
        {
            parts.Add(await Upgrade(lines).RunAsync(Options.Parse("upgrade", args), root, CancellationToken.None));
            await CommitAsync(root, $"part {run + 1}");
            repaired.Add(Read(root, TeamsRefund).Contains(NewNamespace, StringComparison.Ordinal));
        }

        parts.ShouldAllBe(code => code == ExitCode.Done);
        repaired.ShouldBe(allAtOnce ? [true] : [false, true, true, true]);
        // By git, the tip each part leaves on dotskit/template is the base of the next: the team's branch gets one adoption, the first.
        (await Runner.RunCheckedAsync("git", ["log", "--format=%s"], root, CancellationToken.None)).Split('\n')
            .Count(s => s.StartsWith("dotskit: the solution is of the template", StringComparison.Ordinal)).ShouldBe(merge == MergeMode.Git ? 1 : 0);
        Exists(root, OldEvent).ShouldBeFalse();
        Manifest.Load(root)!.Template.ShouldBe(To);
        Read(root, TemplatesOrder).ShouldBe(string.Format(OrderOf, NewNamespace, Property));
        Read(root, TeamsRefund).ShouldBe(string.Format(RefundOf, NewNamespace));
        // The merge brings the template's Order.cs and project to the new namespace - all at once as the template's change, a
        // part at a time as the repair the step's two sides carry - and the repair takes the team's file; all at once it adds
        // the reference the template's project has not got.
        lines.ShouldContain($"  Acme.Shop.Orders.Domain: using {OldNamespace} -> {NewNamespace}{(allAtOnce ? "; reference to Acme.Shop.Common" : "")}");
    }

    [TestCase(MergeMode.Git)]
    [TestCase(MergeMode.Files)]
    public async Task TheLastPartLeftInConflict_KeepsTheUpgradeOpen_AndTheRunAfterTheResolutionFinishesIt(string merge)
    {
        var root = await SolutionAsync();
        // The team renamed the property the next template renames too: the service's part, the last, conflicts.
        Put(root, TemplatesOrder, string.Format(OrderOf, OldNamespace, "Latest"));
        await CommitAsync(root, "the team's name");
        var lines = new List<string>();
        var codes = new List<ExitCode>();
        var outputs = new Outputs(NextProperty: "Previous");

        for (var run = 0; run < 4; run++)
        {
            codes.Add(await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--yes", "--merge", merge]), root, CancellationToken.None));
            if (run < 3)
            {
                await CommitAsync(root, $"part {run + 1}");
            }
        }

        var open = Manifest.Load(root)!;
        Put(root, TemplatesOrder, string.Format(OrderOf, NewNamespace, "Previous"));
        await CommitAsync(root, "the conflict resolved");
        var finished = await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--yes", "--merge", merge]), root, CancellationToken.None);

        codes.ShouldBe([ExitCode.Done, ExitCode.Done, ExitCode.Done, ExitCode.Conflicts]);
        open.Template.ShouldBe(From);
        open.Upgrade!.Done.Count.ShouldBe(4);
        lines.ShouldContain(l => l.Contains("then dotskit upgrade repairs the usings, builds and finishes the upgrade", StringComparison.Ordinal));
        finished.ShouldBe(ExitCode.Done);
        Manifest.Load(root)!.Template.ShouldBe(To);
        Manifest.Load(root)!.Upgrade.ShouldBeNull();
        Read(root, TeamsRefund).ShouldBe(string.Format(RefundOf, NewNamespace));
        lines.ShouldContain(l => l.StartsWith("Finishing the upgrade from the template 2.7.0 to 2.9.0", StringComparison.Ordinal));
    }

    [TestCase(MergeMode.Git)]
    [TestCase(MergeMode.Files)]
    public async Task AServiceFileTheTemplateChangesBeyondTheUsing_MergesCleanAfterThePartOfCommonRepairedIt(string merge)
    {
        var root = await SolutionAsync();
        var lines = new List<string>();
        var outputs = new Outputs(NextAddsAUsing: true);
        var codes = new List<ExitCode>();

        for (var run = 0; run < 4; run++)
        {
            codes.Add(await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--yes", "--merge", merge]), root, CancellationToken.None));
            await CommitAsync(root, $"part {run + 1}");
        }

        codes.ShouldAllBe(code => code == ExitCode.Done);
        Manifest.Load(root)!.Template.ShouldBe(To);
        Read(root, TemplatesOrder).ShouldBe(OtherUsing + string.Format(OrderOf, NewNamespace, Property));
    }

    [TestCase(MergeMode.Git)]
    [TestCase(MergeMode.Files)]
    public async Task AProjectTheTemplateAddsToCommon_IsAPartOfItsOwn_AndTheTypeMovedThereIsFollowed(string merge)
    {
        var root = await SolutionAsync();
        var lines = new List<string>();
        var outputs = new Outputs(NextMovesToANewProject: true);
        var codes = new List<ExitCode>();
        var arrived = new List<bool>();
        var repaired = new List<bool>();

        // shared, the new common/Acme.Shop.Common.Domain the event goes to, then common/Acme.Shop.Common it leaves, capabilities, Acme.Shop.Orders.
        for (var run = 0; run < 5; run++)
        {
            codes.Add(await Upgrade(lines, outputs).RunAsync(Options.Parse("upgrade", ["--yes", "--merge", merge]), root, CancellationToken.None));
            await CommitAsync(root, $"part {run + 1}");
            arrived.Add(Exists(root, MovedEvent));
            repaired.Add(Read(root, TeamsRefund).Contains(NewNamespace, StringComparison.Ordinal));
        }

        codes.ShouldAllBe(code => code == ExitCode.Done);
        lines.ShouldContain(l => l.EndsWith(": shared, 1 of 5.", StringComparison.Ordinal), "the count takes the project the template adds");
        arrived.ShouldBe([false, true, true, true, true]);
        repaired.ShouldBe([false, false, true, true, true]);
        Manifest.Load(root)!.Template.ShouldBe(To);
        Exists(root, OldEvent).ShouldBeFalse();
        Read(root, DomainProject).ShouldBe(Project);
        Read(root, TeamsRefund).ShouldBe(string.Format(RefundOf, NewNamespace));
        Read(root, OrdersProject).Split("Acme.Shop.Common.Domain.csproj").Length.ShouldBe(2, "one reference to the new project, not two");
    }

    /// <summary>The command as Program.cs puts it together, the template's outputs taken from <see cref="Outputs"/>.</summary>
    private static UpgradeCommand Upgrade(List<string> lines, Outputs? outputs = null)
    {
        var projects = new SolutionProjects(Runner);
        var terminal = new Lines(lines);
        var git = new Git(Runner);
        var builder = new Builds();
        var repair = new ReferenceRepair(new RepairWriter(), builder);
        var planner = new Planner(outputs ?? new Outputs(), new ThreeWayMerge(new GitMergeFile(Runner)), projects, new GitRenames(Runner), new GitIgnoredFiles(Runner), repair);
        var writer = new PlanWriter(projects, new GitTemplateMerge(Runner, Sandbox.Self), repair);
        return new UpgradeCommand(planner, new PlanExecution(new PlanReport(terminal), writer, builder, repair, terminal, git), git, terminal, To, onSources: false);
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
        Put(root, Layout.ManifestPath, new Manifest { Template = From, Solution = ["--NamespaceRoot", "Acme"], Services = [["--ServiceNameOrCustom", "Orders"]] }.Serialize());
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
    private sealed record Outputs(string NextProperty = Property, bool NextAddsAUsing = false, bool NextMovesToANewProject = false) : IStaging
    {
        public Task<string> BuildAsync(Manifest manifest, bool withSolutionFile, CancellationToken ct)
        {
            var next = manifest.Template == To;
            var root = Folders.NewTemp("part-repair-output");
            Put(root, CommonProject, Project);
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
