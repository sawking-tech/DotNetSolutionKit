using DotsKit.Commands;
using DotsKit.Io;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>What dotskit upgrade refuses before it plans anything, and the plan it asks for: no template, no processes.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class UpgradeCommandTests
{
    private const string ToolVersion = "2.8.0";

    private sealed class Fakes
    {
        public bool Clean { get; init; } = true;

        /// <summary>The commit git names for a clean tree, call by call; null for a tree not clean.</summary>
        public Queue<string?> Heads { get; init; } = new();

        public string Prefix { get; init; } = "";

        public IReadOnlyList<string> Hidden { get; init; } = [];

        public bool Repository { get; init; } = true;

        /// <summary>What happens to the tree while the plan is built and waits for a yes.</summary>
        public Action? WhilePlanning { get; init; }

        /// <summary>The build of a written part fails, as a team's code the merge broke would.</summary>
        public bool BuildFails { get; init; }

        public PartStep? Step { get; set; }

        public List<PartStep?> Steps { get; } = [];

        /// <summary>The files the writer leaves in conflict; null writes a clean merge.</summary>
        public IReadOnlyList<string>? Conflicts { get; init; }

        /// <summary>The messages the parts were committed with, in order.</summary>
        public List<string> Commits { get; } = [];

        public Manifest? Written { get; set; }

        public Manifest? Next { get; private set; }
        public List<string> Lines { get; } = [];

        public UpgradeCommand Command() =>
            new(new Planner(this), new PlanExecution(new PlanReport(new Terminal(this)), new Writer(this), new Builder(this), new ReferenceRepair(new RepairWriter(), new Builder(this)), new Terminal(this), new Git(this)),
                new Git(this), new NoIgnores(), new Terminal(this), ToolVersion, onSources: false);

        private sealed class NoIgnores : Merging.IIgnoredFiles
        {
            public Task<IReadOnlySet<string>> InAsync(string root, CancellationToken ct) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        }

        private sealed class Planner(Fakes fakes) : IPlanner
        {
            public Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, Merging.MergeMode mode, CancellationToken ct, PartStep? step = null)
            {
                fakes.Next = next;
                fakes.Step = step;
                fakes.Steps.Add(step);
                fakes.WhilePlanning?.Invoke();
                return Task.FromResult(new Plan(root, next, [], new MembershipChange([], [])));
            }
        }

        private sealed class Writer(Fakes fakes) : IPlanWriter
        {
            public Task<WriteResult> WriteAsync(Plan plan, CancellationToken ct)
            {
                var written = plan.Written(fakes.Conflicts is not null);
                fakes.Written = written;
                Directory.CreateDirectory(Path.Combine(plan.Root, ".dotskit"));
                File.WriteAllText(Path.Combine(plan.Root, Layout.ManifestPath), written.Serialize());
                return Task.FromResult(fakes.Conflicts is { } conflicts ? new WriteResult(conflicts, [], []) : WriteResult.Nothing);
            }
        }

        private sealed class Git(Fakes fakes) : IGit
        {
            public Task<bool> IsCleanAsync(string root, CancellationToken ct) => Task.FromResult(fakes.Clean);

            public Task<string?> CleanHeadAsync(string root, CancellationToken ct) =>
                Task.FromResult(fakes.Heads.Count > 0 ? fakes.Heads.Dequeue() : fakes.Clean ? "head" : null);

            public Task<IReadOnlyList<string>> HiddenChangesAsync(string root, CancellationToken ct) => Task.FromResult(fakes.Hidden);

            public Task<bool> IsRepositoryAsync(string root, CancellationToken ct) => Task.FromResult(fakes.Repository);

            public Task<string> PrefixAsync(string root, CancellationToken ct) => Task.FromResult(fakes.Prefix);

            public Task<(string Commit, string Previous)?> CommitUncommittedAsync(string root, string message, CancellationToken ct) => Task.FromResult<(string, string)?>(null);

            public Task UncommitAsync(string root, string commit, CancellationToken ct) => Task.CompletedTask;

            public Task<bool> MergeInProgressAsync(string root, CancellationToken ct) => Task.FromResult(false);

            public Task CommitAsync(string root, string message, CancellationToken ct)
            {
                fakes.Commits.Add(message);
                return Task.CompletedTask;
            }
        }

        private sealed class Builder(Fakes fakes) : IBuilder
        {
            public Task<ProcessResult> BuildAsync(string root, CancellationToken ct) => Task.FromResult(fakes.BuildFails ? new ProcessResult(1, "", "error") : new ProcessResult(0, "", ""));
        }

        private sealed class Terminal(Fakes fakes) : ITerminal
        {
            public void Info(string line) => fakes.Lines.Add(line);

            public void Error(string line) => fakes.Lines.Add(line);

            public bool Confirm(string question) => false;
        }

        public Task<ExitCode> RunAsync(string root) => Command().RunAsync(Options.Parse(["--yes", "--all-at-once"]), root, CancellationToken.None);
    }

    /// <summary>A solution with an All.sln and, when <paramref name="template"/> is given, a manifest of that version.</summary>
    private static string Solution(string? template)
    {
        var root = Folders.NewTemp("upgrade-command");
        Directory.CreateDirectory(Layout.ServicesOf(root));
        File.WriteAllText(Path.Combine(Layout.ServicesOf(root), "ST.Demo" + Layout.SolutionSuffix), "");
        Directory.CreateDirectory(Path.Combine(Layout.ServicesOf(root), "ST.Demo.Orders"));
        File.WriteAllText(Path.Combine(Layout.ServicesOf(root), "ST.Demo.Orders", "Program.cs"), "");
        if (template is null)
        {
            return root;
        }

        var manifest = new Manifest { Template = template, Solution = ["--NamespaceRoot", "ST", "--ProductName", "Demo"], Services = [["--ServiceNameOrCustom", "Orders"]] };
        Directory.CreateDirectory(Path.Combine(root, ".dotskit"));
        File.WriteAllText(Path.Combine(root, Layout.ManifestPath), manifest.Serialize());
        return root;
    }

    [Test]
    public async Task ASolutionWithoutAManifest_IsSentToInit()
    {
        (await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(Solution(null)))).Message.ShouldContain("dotskit init");
    }

    [Test]
    public async Task ASolutionOfTheToolsVersion_HasNothingToUpgrade()
    {
        var fakes = new Fakes();

        (await fakes.RunAsync(Solution(ToolVersion))).ShouldBe(ExitCode.Done);

        fakes.Next.ShouldBeNull();
        fakes.Lines.ShouldContain($"The solution is of the template {ToolVersion} already.");
    }

    [Test]
    public async Task AnEarlierMinor_IsPlannedToTheToolsVersion()
    {
        var fakes = new Fakes();

        (await fakes.RunAsync(Solution("2.7.0"))).ShouldBe(ExitCode.Done);

        fakes.Next!.Template.ShouldBe(ToolVersion);
        fakes.Lines.ShouldContain(UpgradeGuide.Of("2.7.0", ToolVersion));
    }

    [Test]
    public async Task ANewerSolution_NamesTheToolUpdate()
    {
        (await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(Solution("2.9.0")))).Message.ShouldContain("dotnet tool update -g SawKing.DotsKit.Tool");
    }

    [Test]
    public async Task ASolutionOf1x_IsSentToTheNotes_NotToAToolThatWouldRefuseItAgain()
    {
        var refused = await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(Solution("1.4.0")));

        refused.Message.ShouldContain("docs/getting-started/upgrading.md");
        refused.Message.ShouldNotContain("dotnet tool update");
    }

    [Test]
    public async Task AParameterOfTheTemplate_IsRefused()
    {
        var refused = await Should.ThrowAsync<ArgumentException>(() =>
            new Fakes().Command().RunAsync(Options.Parse(["--Storage", "true"]), Solution("2.7.0"), CancellationToken.None));

        refused.Message.ShouldContain("dotskit new");
    }

    [Test]
    public async Task ForceOnAnUncommittedTree_IsRefused_AsItWouldLoseTheTeamsLines()
    {
        var fakes = new Fakes { Clean = false };

        (await Should.ThrowAsync<ArgumentException>(() =>
            fakes.Command().RunAsync(Options.Parse(["--yes", "--allow-dirty", "--force"]), Solution("2.7.0"), CancellationToken.None)))
            .Message.ShouldContain("--force needs a clean tree");
        fakes.Next.ShouldBeNull();
    }

    [Test]
    public async Task ATreeChangedOrCommittedAfterTheStart_IsNotWrittenOver()
    {
        // Clean at the start; before the write, a file is saved, or a commit made over a clean tree.
        foreach (var later in new string?[] { null, "a commit made meanwhile" })
        {
            var fakes = new Fakes { Heads = new(["head", later]) };

            (await Should.ThrowAsync<InvalidOperationException>(() => fakes.RunAsync(Solution("2.7.0")))).Message.ShouldContain("changed after the command started");
        }
    }

    [Test]
    public async Task AnUpgrade_TakesTheSharedPartFirst_ThenTheService_AndTheLastMovesTheVersion()
    {
        var root = Solution("2.7.0");
        var fakes = new Fakes();

        (await fakes.Command().RunAsync(Options.Parse(["--yes"]), root, CancellationToken.None)).ShouldBe(ExitCode.Done);

        fakes.Steps.Select(s => s!.Part).ShouldBe([UpgradeParts.Shared, "ST.Demo.Orders"]);
        fakes.Lines.ShouldContain(l => l.EndsWith(": shared, 1 of 2.", StringComparison.Ordinal));
        fakes.Lines.ShouldContain(l => l.EndsWith(": ST.Demo.Orders, 2 of 2, the last.", StringComparison.Ordinal));
        fakes.Commits.ShouldBe([$"dotskit: the upgrade to {ToolVersion}, part {UpgradeParts.Shared}", $"dotskit: the upgrade to {ToolVersion}, finished"]);
        var merged = Manifest.Load(root)!;
        merged.Template.ShouldBe(ToolVersion);
        merged.Upgrade.ShouldBeNull();
    }

    [Test]
    public async Task AnUpgrade_StopsAtTheFirstPartInConflict_NamesWhatIsLeftAndHowItGoesOn()
    {
        var root = Solution("2.7.0");
        var fakes = new Fakes { Conflicts = ["src/services/ST.Demo.Orders/Program.cs"] };

        (await fakes.Command().RunAsync(Options.Parse(["--yes"]), root, CancellationToken.None)).ShouldBe(ExitCode.Conflicts);

        fakes.Steps.ShouldHaveSingleItem();
        fakes.Commits.ShouldBeEmpty();
        fakes.Lines.ShouldContain($"Conflicts in {UpgradeParts.Shared}: 2 of 2 part(s) of the upgrade to {ToolVersion} left - resolve them, commit, then continue with dotskit upgrade --continue.");
        // The conflicts keep the upgrade open, and the in-flight part is no part of Done until its merge is committed.
        var open = Manifest.Load(root)!.Upgrade!;
        open.To.ShouldBe(ToolVersion);
        open.Done.ShouldBeEmpty();
        open.Parts.ShouldBe([UpgradeParts.Shared, "ST.Demo.Orders"]);
    }

    [Test]
    public async Task AnUpgradeConflictsInItsLastPart_SaysTheRunAfterTheResolutionFinishesIt()
    {
        var root = Solution("2.7.0");
        var manifest = Manifest.Load(root)! with { Upgrade = new UpgradeInProgress { To = ToolVersion, Done = [UpgradeParts.Shared] } };
        File.WriteAllText(Path.Combine(root, Layout.ManifestPath), manifest.Serialize());
        var fakes = new Fakes { Conflicts = ["src/services/ST.Demo.Orders/Program.cs"] };

        (await fakes.Command().RunAsync(Options.Parse(["--yes", "--continue"]), root, CancellationToken.None)).ShouldBe(ExitCode.Conflicts);

        fakes.Lines.ShouldContain($"Conflicts in ST.Demo.Orders, the last part of the upgrade to {ToolVersion}: resolve them, commit, then dotskit upgrade --continue repairs the usings, builds and finishes the upgrade.");
        // The last part counts as merged only once its merge is committed: the stop leaves it out of Done.
        Manifest.Load(root)!.Upgrade!.Done.ShouldBe([UpgradeParts.Shared]);
    }

    [Test]
    public async Task APartWhoseBuildFails_IsNotCommitted_AndNamesHowToGoOn()
    {
        var root = Solution("2.7.0");
        var fakes = new Fakes { BuildFails = true };

        (await fakes.Command().RunAsync(Options.Parse(["--yes"]), root, CancellationToken.None)).ShouldBe(ExitCode.Failed);

        fakes.Steps.ShouldHaveSingleItem();
        fakes.Commits.ShouldBeEmpty();
        fakes.Lines.ShouldContain($"A part of the upgrade to {ToolVersion} is written, and the build fails: fix the build, commit the part, then dotskit upgrade --continue takes the next part.");
    }

    [Test]
    public async Task ContinueWithoutAnUpgradeInProgress_IsRefused()
    {
        (await Should.ThrowAsync<InvalidOperationException>(() =>
            new Fakes().Command().RunAsync(Options.Parse(["--yes", "--continue"]), Solution("2.7.0"), CancellationToken.None)))
            .Message.ShouldContain("No upgrade is in progress");
    }

    [Test]
    public async Task Continue_IsAnOptionOfUpgradeOnly()
    {
        (await Should.ThrowAsync<ArgumentException>(() => Task.Run(() => Options.Parse("new", ["--continue"]))))
            .Message.ShouldContain("dotskit upgrade");
    }

    [Test]
    public async Task AnUpgradeBegunByANewerTool_IsRefusedByAnOlderOne_BeforeAnythingIsPlanned()
    {
        var root = Solution("2.7.0");
        var manifest = Manifest.Load(root)! with { Upgrade = new UpgradeInProgress { To = "2.9.0", Done = [UpgradeParts.Shared] } };
        File.WriteAllText(Path.Combine(root, Layout.ManifestPath), manifest.Serialize());
        var fakes = new Fakes();

        var error = await Should.ThrowAsync<InvalidOperationException>(() => fakes.Command().RunAsync(Options.Parse(["--yes"]), root, CancellationToken.None));

        error.Message.ShouldContain($"newer than dotskit {ToolVersion}");
        fakes.Step.ShouldBeNull();
    }

    [Test]
    public async Task UnderAllowDirty_AFileSavedAfterTheStart_IsNotWrittenOver_AndAnUntouchedTreeIs()
    {
        var root = Solution("2.7.0");
        var program = Path.Combine(Layout.ServicesOf(root), "ST.Demo.Orders", "Program.cs");
        var saved = new Fakes { WhilePlanning = () => File.WriteAllText(program, "// saved in the IDE while the plan waited") };
        var untouched = new Fakes();
        var dirty = Options.Parse(["--yes", "--allow-dirty", "--all-at-once"]);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => saved.Command().RunAsync(dirty, root, CancellationToken.None));
        await untouched.Command().RunAsync(dirty, root, CancellationToken.None);

        refused.Message.ShouldContain("a file of it or its commit changed after the command started");
        saved.Written.ShouldBeNull();
        untouched.Written.ShouldNotBeNull();
    }

    [Test]
    public async Task AFileGitHidesTheChangesOf_IsNamed_BeforeAnythingIsPlanned()
    {
        var fakes = new Fakes { Hidden = ["src/services/ST.Demo.Orders/appsettings.Development.json"] };

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => fakes.RunAsync(Solution("2.7.0")));

        refused.Message.ShouldContain("appsettings.Development.json (assume-unchanged or skip-worktree)");
        refused.Message.ShouldContain("git update-index --no-assume-unchanged");
        fakes.Next.ShouldBeNull();
    }

    [Test]
    public async Task AnUncleanTree_IsRefused_BeforeAnythingIsPlanned()
    {
        var fakes = new Fakes { Clean = false };

        (await Should.ThrowAsync<InvalidOperationException>(() => fakes.RunAsync(Solution("2.7.0")))).Message.ShouldContain("--allow-dirty");
        fakes.Next.ShouldBeNull();
    }

    [Test]
    public async Task ASolutionInAFolderOfItsRepository_IsRefusedTheMerge_BeforeAPlan()
    {
        var refused = new Fakes { Prefix = "backend/" };

        (await Should.ThrowAsync<InvalidOperationException>(() => refused.RunAsync(Solution("2.7.0")))).Message.ShouldContain("in backend of its git repository");

        refused.Next.ShouldBeNull();
    }
}

