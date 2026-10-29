using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>What the writer of a plan says when a step after the write stops: the merge by files has no undo after it.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class PlanWriterTests
{
    private const string Common = "src/common/Acme.Shop.Common/Acme.Shop.Common.csproj";
    private const string Framework = "src/framework/Acme.Shop.Framework.Events/Acme.Shop.Framework.Events.csproj";
    private const string Domain = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain";

    private static readonly TypeMap Moved = TypeMap.Between(
        TypeIndex.Of([(Common, "namespace Acme.Shop.Common.Events;\n\npublic sealed class DomainEvent;\n")]),
        TypeIndex.Of([(Framework, "namespace Acme.Shop.Framework.Events;\n\npublic sealed class DomainEvent;\n")]));

    /// <summary>The repair, stopped by Ctrl+C at its first file.</summary>
    private sealed class Cancelled : IRepairWriter
    {
        public Task<IReadOnlyList<string>> ApplyAsync(IReadOnlyList<Repair> repairs, CancellationToken ct) => throw new OperationCanceledException();
    }

    private sealed class NoBuild : IBuilder
    {
        public Task<ProcessResult> BuildAsync(string root, CancellationToken ct) => Task.FromResult(new ProcessResult(0, "", ""));
    }

    /// <summary>The merge by files never reaches git.</summary>
    private sealed class NoGit : IGitMerge
    {
        public IReadOnlySet<string> LeftOut { get; } = new HashSet<string>();

        public Task<IReadOnlyList<string>> MergeAsync(string root, TemplateCommit @base, TemplateCommit next, bool preferTemplate, CancellationToken ct) =>
            throw new InvalidOperationException("the merge by files reached git");

        public Task StageAsync(string root, IReadOnlyList<string> paths, CancellationToken ct) => Task.CompletedTask;

        public Task AbortAsync(string root, CancellationToken ct) => Task.CompletedTask;
    }

    [Test]
    public async Task ARepairCancelled_AfterTheMergeByFilesIsWritten_SaysTheFilesAreWritten()
    {
        var root = Folders.NewTemp("plan-writer");
        Directory.CreateDirectory(Path.Combine(root, Domain));
        await File.WriteAllTextAsync(Path.Combine(root, Domain, "Acme.Shop.Orders.Domain.csproj"), "<Project />\n");
        await File.WriteAllTextAsync(Path.Combine(root, Domain, "Order.cs"), "using Acme.Shop.Common.Events;\n");
        var manifest = new Manifest { Template = "2.0.0", Solution = ["--NamespaceRoot", "Acme", "--ProductName", "Shop"], Services = [] };
        var plan = new Plan(root, manifest, [], new MembershipChange([], []), Types: Moved);
        var writer = new PlanWriter(new SolutionProjects(new ProcessRunner()), new NoGit(), new ReferenceRepair(new Cancelled(), new NoBuild()));

        var stopped = await Should.ThrowAsync<InvalidOperationException>(() => writer.WriteAsync(plan, CancellationToken.None));

        stopped.Message.ShouldStartWith("The files are written");
        stopped.Message.ShouldContain("cancelled");
        File.Exists(Path.Combine(root, Layout.ManifestPath)).ShouldBeTrue();
    }
}
