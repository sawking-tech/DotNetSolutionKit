using DotsKit.Commands;
using DotsKit.Generation;
using DotsKit.Io;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>What dotskit new refuses before it plans anything, and what it does with the answer: no template, no processes.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class NewCommandTests
{
    private const string ToolVersion = "2.8.0";

    private sealed class Fakes
    {
        public bool Clean { get; init; } = true;
        public bool Answer { get; init; }
        public int Planned { get; private set; }
        public int Written { get; private set; }
        public List<string> Lines { get; } = [];

        public NewCommand Command() =>
            new(new Planner(this), new PlanExecution(new PlanReport(new Terminal(this)), new Writer(this), new Builder(), new ReferenceRepair(new RepairWriter(new Io.ProcessRunner()), new Builder()), new Terminal(this)), new Git(this), ToolVersion, onSources: false);

        private sealed class Planner(Fakes fakes) : IPlanner
        {
            public Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, Merging.MergeMode mode, CancellationToken ct)
            {
                fakes.Planned++;
                return Task.FromResult(new Plan(root, next, [], new MembershipChange([], [])));
            }
        }

        private sealed class Writer(Fakes fakes) : IPlanWriter
        {
            public Task<WriteResult> WriteAsync(Plan plan, CancellationToken ct)
            {
                fakes.Written++;
                return Task.FromResult(WriteResult.Nothing);
            }
        }

        private sealed class Git(Fakes fakes) : IGit
        {
            public Task<bool> IsCleanAsync(string root, CancellationToken ct) => Task.FromResult(fakes.Clean);
        }

        private sealed class Builder : IBuilder
        {
            public Task<ProcessResult> BuildAsync(string root, CancellationToken ct) => Task.FromResult(new ProcessResult(0, "", ""));
        }

        private sealed class Terminal(Fakes fakes) : ITerminal
        {
            public void Info(string line) => fakes.Lines.Add(line);

            public void Error(string line) => fakes.Lines.Add(line);

            public bool Confirm(string question) => fakes.Answer;
        }
    }

    private static Options Args(params string[] args) => Options.Parse([.. args, "--no-build"]);

    /// <summary>A solution with an All.sln and, when <paramref name="template"/> is given, a manifest of that version.</summary>
    private static string Solution(string? template)
    {
        var root = Folders.NewTemp("new-command");
        Directory.CreateDirectory(Layout.ServicesOf(root));
        File.WriteAllText(Path.Combine(Layout.ServicesOf(root), "Acme.Shop" + Layout.SolutionSuffix), "");
        if (template is null)
            return root;
        var manifest = new Manifest
        {
            Template = template,
            Solution = ["--NamespaceRoot", "Acme", "--ProductName", "Shop"],
            Services = [["--ServiceNameOrCustom", "Orders", "--HttpPort", "5000"]],
        };
        Directory.CreateDirectory(Path.Combine(root, ".dotskit"));
        File.WriteAllText(Path.Combine(root, Layout.ManifestPath), manifest.Serialize());
        return root;
    }

    private static string ThisVersion => ToolVersion;

    [Test]
    public async Task Should_Refuse_When_TheSolutionHasNoManifest()
    {
        var fakes = new Fakes();

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => fakes.Command().RunAsync(Args("-S", "Billing"), Solution(null), CancellationToken.None));

        refused.Message.ShouldContain(Layout.ManifestPath);
        fakes.Planned.ShouldBe(0);
    }

    [Test]
    public async Task Should_Refuse_When_TheSolutionIsOfAnotherVersion()
    {
        var fakes = new Fakes();

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => fakes.Command().RunAsync(Args("-S", "Billing"), Solution("2.7.0"), CancellationToken.None));

        refused.Message.ShouldContain("dotskit upgrade");
        fakes.Planned.ShouldBe(0);
    }

    [Test]
    public async Task Should_NameTheToolUpdate_When_TheSolutionIsNewerThanTheTool()
    {
        var fakes = new Fakes();

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => fakes.Command().RunAsync(Args("-S", "Billing"), Solution("2.9.0"), CancellationToken.None));

        refused.Message.ShouldContain("dotnet tool update -g SawKing.DotsKit.Tool");
    }

    [Test]
    public async Task Should_Refuse_When_ACommandGivesTheSolutionAnotherDatabase()
    {
        var fakes = new Fakes();

        var refused = await Should.ThrowAsync<ArgumentException>(() =>
            fakes.Command().RunAsync(Args("-S", "Billing", "--Database", "mssql"), Solution(ThisVersion), CancellationToken.None));

        refused.Message.ShouldContain("Database (postgres)");
        fakes.Planned.ShouldBe(0);
    }

    [Test]
    public async Task Should_Refuse_When_ANewSolutionWouldGoOverFilesOutsideACleanTree()
    {
        var folder = Folders.NewTemp("new-command");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "the team's");
        var fakes = new Fakes { Clean = false };

        await Should.ThrowAsync<InvalidOperationException>(() => fakes.Command().RunAsync(Args("-N", "Acme", "-S", "Orders"), folder, CancellationToken.None));

        fakes.Planned.ShouldBe(0);
    }

    [Test]
    public async Task Should_MakeANewSolution_When_TheFolderIsEmpty()
    {
        var fakes = new Fakes { Clean = false, Answer = true };

        var code = await fakes.Command().RunAsync(Args("-N", "Acme", "-S", "Orders"), Folders.NewTemp("new-command"), CancellationToken.None);

        code.ShouldBe(ExitCode.Done);
        fakes.Written.ShouldBe(1);
    }

    [Test]
    public async Task Should_WriteNothing_And_SayHowToGoOn_When_TheAnswerIsNo()
    {
        var fakes = new Fakes();

        var code = await fakes.Command().RunAsync(Args("-S", "Billing"), Solution(ThisVersion), CancellationToken.None);

        code.ShouldBe(ExitCode.Failed);
        fakes.Written.ShouldBe(0);
        fakes.Lines.ShouldContain(l => l.Contains("--yes"));
    }

    [Test]
    public void Should_Refuse_When_TheFolderHasTwoSolutions()
    {
        var root = Solution(null);
        File.WriteAllText(Path.Combine(Layout.ServicesOf(root), "Other" + Layout.SolutionSuffix), "");

        Should.Throw<InvalidOperationException>(() => SolutionRoot.HasSolution(root)).Message.ShouldContain("2");
    }
}
