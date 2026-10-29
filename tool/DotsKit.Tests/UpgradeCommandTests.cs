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
        public Manifest? Next { get; private set; }
        public List<string> Lines { get; } = [];

        public UpgradeCommand Command() =>
            new(new Planner(this), new PlanExecution(new PlanReport(new Terminal(this)), new Writer(), new Builder(), new ReferenceRepair(new RepairWriter(new ProcessRunner()), new Builder()), new Terminal(this)),
                new Git(this), new Terminal(this), ToolVersion, onSources: false);

        private sealed class Planner(Fakes fakes) : IPlanner
        {
            public Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, Merging.MergeMode mode, CancellationToken ct)
            {
                fakes.Next = next;
                return Task.FromResult(new Plan(root, next, [], new MembershipChange([], [])));
            }
        }

        private sealed class Writer : IPlanWriter
        {
            public Task<WriteResult> WriteAsync(Plan plan, CancellationToken ct) => Task.FromResult(WriteResult.Nothing);
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

            public bool Confirm(string question) => false;
        }

        public Task<ExitCode> RunAsync(string root) => Command().RunAsync(Options.Parse(["--yes"]), root, CancellationToken.None);
    }

    /// <summary>A solution with an All.sln and, when <paramref name="template"/> is given, a manifest of that version.</summary>
    private static string Solution(string? template)
    {
        var root = Folders.NewTemp("upgrade-command");
        Directory.CreateDirectory(Layout.ServicesOf(root));
        File.WriteAllText(Path.Combine(Layout.ServicesOf(root), "Acme.Shop" + Layout.SolutionSuffix), "");
        if (template is null)
        {
            return root;
        }

        var manifest = new Manifest { Template = template, Solution = ["--NamespaceRoot", "Acme", "--ProductName", "Shop"], Services = [["--ServiceNameOrCustom", "Orders"]] };
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
    public async Task AnUncleanTree_IsRefused_BeforeAnythingIsPlanned()
    {
        var fakes = new Fakes { Clean = false };

        (await Should.ThrowAsync<InvalidOperationException>(() => fakes.RunAsync(Solution("2.7.0")))).Message.ShouldContain("--allow-dirty");
        fakes.Next.ShouldBeNull();
    }
}
