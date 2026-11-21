using DotsKit.Ignoring;
using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>A merge by git, recorded: what the writer stages, and with which flags, so the order of a write shows without git.</summary>
internal sealed class RecordingMerge : IGitMerge
{
    public IReadOnlySet<string> LeftOut { get; } = new HashSet<string>();

    public IReadOnlyList<string> Taken => [];

    /// <summary>Every staging by the write, in order: the paths and whether the staging forces them past the solution's git.</summary>
    public List<(IReadOnlyList<string> Paths, bool Force)> Staged { get; } = [];

    public Task<IReadOnlyList<string>> MergeAsync(string root, TemplateCommit @base, TemplateCommit next, bool preferTemplate, bool markBase, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(["conflict.cs"]);

    public Task StageAsync(string root, IReadOnlyList<string> paths, CancellationToken ct, bool force = false)
    {
        Staged.Add((paths, force));
        return Task.CompletedTask;
    }

    public Task AbortAsync(string root, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>What the writer of a plan says when a step after the write stops: the tool's own write has no undo after it.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class PlanWriterTests
{
    private const string Common = "src/common/ST.Demo.Common/ST.Demo.Common.csproj";
    private const string Framework = "src/framework/ST.Demo.Framework.Events/ST.Demo.Framework.Events.csproj";
    private const string Domain = "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain";

    private static readonly TypeMap Moved = TypeMap.Between(
        TypeIndex.Of([(Common, "namespace ST.Demo.Common.Events;\n\npublic sealed class DomainEvent;\n")]),
        TypeIndex.Of([(Framework, "namespace ST.Demo.Framework.Events;\n\npublic sealed class DomainEvent;\n")]));

    /// <summary>The repair, stopped by Ctrl+C at its first file.</summary>
    private sealed class Cancelled : IRepairWriter
    {
        public Task<IReadOnlyList<Repair>> ApplyAsync(IReadOnlyList<Repair> repairs, CancellationToken ct) => throw new OperationCanceledException();
    }

    private sealed class NoBuild : IBuilder
    {
        public Task<ProcessResult> BuildAsync(string root, CancellationToken ct) => Task.FromResult(new ProcessResult(0, "", ""));
    }

    /// <summary>The tool's own write never reaches git.</summary>
    private sealed class NoGit : IGitMerge
    {
        public IReadOnlySet<string> LeftOut { get; } = new HashSet<string>();

        public IReadOnlyList<string> Taken => [];

        public Task<IReadOnlyList<string>> MergeAsync(string root, TemplateCommit @base, TemplateCommit next, bool preferTemplate, bool markBase, CancellationToken ct) =>
            throw new InvalidOperationException("the tool's own write reached git");

        public Task StageAsync(string root, IReadOnlyList<string> paths, CancellationToken ct, bool force = false) => Task.CompletedTask;

        public Task AbortAsync(string root, CancellationToken ct) => Task.CompletedTask;
    }

    [Test]
    public async Task ARepairCancelled_AfterTheMergeByFilesIsWritten_SaysTheFilesAreWritten()
    {
        var root = Folders.NewTemp("plan-writer");
        Directory.CreateDirectory(Path.Combine(root, Domain));
        await File.WriteAllTextAsync(Path.Combine(root, Domain, "ST.Demo.Orders.Domain.csproj"), "<Project />\n");
        await File.WriteAllTextAsync(Path.Combine(root, Domain, "Order.cs"), "using ST.Demo.Common.Events;\n");
        var manifest = new Manifest { Template = "2.0.0", Solution = ["--NamespaceRoot", "ST", "--ProductName", "Demo"], Services = [] };
        var plan = new Plan(root, manifest, [], new MembershipChange([], []), Types: Moved);
        var writer = new PlanWriter(new SolutionProjects(new ProcessRunner()), new NoGit(), new ReferenceRepair(new Cancelled(), new NoBuild()));

        var stopped = await Should.ThrowAsync<InvalidOperationException>(() => writer.WriteAsync(plan, CancellationToken.None));

        stopped.Message.ShouldStartWith("The files are written");
        stopped.Message.ShouldContain("cancelled");
        File.Exists(Path.Combine(root, Layout.ManifestPath)).ShouldBeTrue();
    }

    /// <summary>
    /// A file of the rules the tool rewrote takes its place in the merge commit as the team's own file, whatever the
    /// solution's git hides: the staging forces it past the solution's rules, for the file at the root and for one in a
    /// deeper folder alike.
    /// </summary>
    [Test]
    public async Task ARulesFileTheToolRewrote_IsStagedPastTheSolutionsGit_AtTheDepthItSitsIn()
    {
        var root = Folders.NewTemp("plan-writer-rules");
        Directory.CreateDirectory(Path.Combine(root, Domain));
        await File.WriteAllTextAsync(Path.Combine(root, Domain, "ST.Demo.Orders.Domain.csproj"), "<Project />\n");
        Directory.CreateDirectory(Path.Combine(root, Layout.ServicesFolder));
        await File.WriteAllTextAsync(Path.Combine(root, Layout.ServicesFolder, "ST.Demo.All.sln"), "\n");
        await File.WriteAllTextAsync(DotskitIgnores.PathOf(root), "src/common/X/@2.9\n");
        Directory.CreateDirectory(Path.Combine(root, "src/common/X"));
        await File.WriteAllTextAsync(DotskitIgnores.PathOf(root, "src/common/X"), "Locked.cs@major\n");
        var manifest = new Manifest { Template = "2.9.0", Solution = ["--NamespaceRoot", "ST", "--ProductName", "Demo"], Services = [] };
        var rootFile = DotskitIgnore.Parse("", "src/common/X/@2.9\n");
        var deeperFile = DotskitIgnore.Parse("src/common/X", "Locked.cs@major\n");
        var ignore = new IgnoreDecision(new HashSet<string>(), [.. rootFile.Rules, .. deeperFile.Rules], "3.0.0", new Dictionary<IgnoreRule, IReadOnlySet<string>>());
        var next = Folders.NewTemp("plan-writer-next");
        var plan = new Plan(root, manifest, [], new MembershipChange([], []), Types: Moved, NextTree: next, Ignore: ignore,
            ByGit: (new TemplateCommit(Folders.NewTemp("plan-writer-base"), "2.9.0"), new TemplateCommit(next, "3.0.0")));
        var git = new RecordingMerge();
        var writer = new PlanWriter(new SolutionProjects(new ProcessRunner()), git, new ReferenceRepair(new RepairWriter(), new NoBuild()));

        await writer.WriteAsync(plan, CancellationToken.None);

        var forced = git.Staged.Where(s => s.Force).SelectMany(s => s.Paths).ToList();
        forced.ShouldBe([DotskitIgnores.FileName, "src/common/X/" + DotskitIgnores.FileName]);
        File.ReadAllText(DotskitIgnores.PathOf(root)).ShouldContain("# src/common/X/@3.0 ", customMessage: "the pin of 2.9 is spent, at the line the upgrade went to");
        File.ReadAllText(DotskitIgnores.PathOf(root, "src/common/X")).ShouldContain("# Locked.cs@3", customMessage: "each file takes its own share of the spent pins");
    }
}
