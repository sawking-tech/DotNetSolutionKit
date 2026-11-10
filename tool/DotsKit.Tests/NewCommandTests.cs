using DotsKit.Commands;
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

        public string Prefix { get; init; } = "";

        public IReadOnlyList<string> Hidden { get; init; } = [];

        public bool Repository { get; init; } = true;

        public bool Answer { get; init; }
        public int Planned { get; private set; }
        public int Written { get; private set; }
        public int TakenUncommitted { get; private set; }
        public Manifest? Next { get; private set; }
        public List<string> Lines { get; } = [];

        public NewCommand Command() =>
            new(new Planner(this), new PlanExecution(new PlanReport(new Terminal(this)), new Writer(this), new Builder(), new ReferenceRepair(new RepairWriter(), new Builder()), new Terminal(this), new Git(this)), new Git(this), ToolVersion, onSources: false);

        private sealed class Planner(Fakes fakes) : IPlanner
        {
            public Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, Merging.MergeMode mode, CancellationToken ct, PartStep? step = null)
            {
                fakes.Planned++;
                fakes.Next = next;
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

            public Task<string?> CleanHeadAsync(string root, CancellationToken ct) => Task.FromResult(fakes.Clean ? "head" : null);

            public Task<IReadOnlyList<string>> HiddenChangesAsync(string root, CancellationToken ct) => Task.FromResult(fakes.Hidden);

            public Task<bool> IsRepositoryAsync(string root, CancellationToken ct) => Task.FromResult(fakes.Repository);

            public Task<string> PrefixAsync(string root, CancellationToken ct) => Task.FromResult(fakes.Prefix);

            public Task<(string Commit, string Previous)?> CommitUncommittedAsync(string root, string message, CancellationToken ct)
            {
                fakes.TakenUncommitted++;
                return Task.FromResult<(string, string)?>(fakes.Clean ? null : ("taken", "head"));
            }

            public Task UncommitAsync(string root, string commit, CancellationToken ct) => Task.CompletedTask;

            public Task<bool> MergeInProgressAsync(string root, CancellationToken ct) => Task.FromResult(false);

            public Task CommitAsync(string root, string message, CancellationToken ct) => Task.CompletedTask;
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
        File.WriteAllText(Path.Combine(Layout.ServicesOf(root), "ST.Demo" + Layout.SolutionSuffix), "");
        if (template is null)
        {
            return root;
        }

        var manifest = new Manifest
        {
            Template = template,
            Solution = ["--NamespaceRoot", "ST", "--ProductName", "Demo"],
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
    public async Task AServiceAddedToASolutionInAFolderOfItsRepository_IsRefusedTheMerge_BeforeAPlan()
    {
        var fakes = new Fakes { Prefix = "backend/" };

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => fakes.Command().RunAsync(Args("-S", "Billing"), Solution(ThisVersion), CancellationToken.None));

        refused.Message.ShouldContain("own repository");
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

    [TestCase("Infisical")]
    [TestCase("Vault")]
    public async Task Should_KeepTheExistingServicesSecretStoreChoice_When_AddingAnotherCapability(string store)
    {
        var root = Solution(ThisVersion);
        var current = Manifest.Load(root)!;
        current = current with
        {
            Solution = [.. current.Solution, "--" + store, "true"],
            Services = [["--ServiceNameOrCustom", "Orders", "--" + store, "false"]],
        };
        File.WriteAllText(Path.Combine(root, Layout.ManifestPath), current.Serialize());
        var fakes = new Fakes { Answer = true };

        await fakes.Command().RunAsync(Args("-S", "Orders", "--Storage", "true"), root, CancellationToken.None);

        fakes.Next!.FindService("Orders")![store].ShouldBe("false");
    }

    [Test]
    public async Task Should_Refuse_When_ANewSolutionWouldGoOverFilesOutsideACleanTree()
    {
        var folder = Folders.NewTemp("new-command");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "the team's");
        var fakes = new Fakes { Clean = false };

        await Should.ThrowAsync<InvalidOperationException>(() => fakes.Command().RunAsync(Args("-N", "ST", "-S", "Orders"), folder, CancellationToken.None));

        fakes.Planned.ShouldBe(0);
    }

    [Test]
    public async Task Should_RefuseANewSolutionBesideFiles_When_TheFolderHasNoRepository_NamingHowToMakeOne()
    {
        var folder = Folders.NewTemp("new-command");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "the team's");
        var fakes = new Fakes { Repository = false };

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => fakes.Command().RunAsync(Args("-N", "ST", "-S", "Orders"), folder, CancellationToken.None));

        refused.Message.ShouldContain("git init");
        refused.Message.ShouldContain("gitleaks");
        fakes.Planned.ShouldBe(0);
    }

    [Test]
    public async Task Should_MergeANewSolutionBesideFiles_ByGit_When_TheFolderIsARepositoryWithACleanTree()
    {
        var folder = Folders.NewTemp("new-command");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "the team's");
        var fakes = new Fakes { Answer = true };

        var code = await fakes.Command().RunAsync(Args("-N", "ST", "-S", "Orders"), folder, CancellationToken.None);

        code.ShouldBe(ExitCode.Done);
        fakes.Planned.ShouldBe(1);
        fakes.Written.ShouldBe(1);
    }

    [Test]
    public async Task Should_TakeUncommittedChanges_BesideFiles_When_AllowDirtyIsNamed()
    {
        var folder = Folders.NewTemp("new-command");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "the team's");
        var fakes = new Fakes { Clean = false, Answer = true };

        var code = await fakes.Command().RunAsync(Args("-N", "ST", "-S", "Orders", "--allow-dirty"), folder, CancellationToken.None);

        code.ShouldBe(ExitCode.Done);
        fakes.TakenUncommitted.ShouldBe(1);
        fakes.Planned.ShouldBe(1);
    }

    [Test]
    public async Task Should_MakeANewSolution_When_TheFolderIsEmpty()
    {
        var fakes = new Fakes { Clean = false, Answer = true };

        var code = await fakes.Command().RunAsync(Args("-N", "ST", "-S", "Orders"), Folders.NewTemp("new-command"), CancellationToken.None);

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

