using DotsKit.Generation;
using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.PackageUpdates;
using DotsKit.Solutions;

namespace DotsKit.Planning;

/// <summary>
/// Builds a plan: what the template gave the solution (the base, from the current manifest), what it gives now
/// (from the next one), and the three-way merge of the two with the solution's own files. The solution's package
/// policy is put into the template's side first, and the files the template moved are followed.
/// </summary>
internal interface IPlanner
{
    Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, MergeMode mode, CancellationToken ct, PartStep? step = null);
}

internal sealed class Planner(IStaging staging, ThreeWayMerge merge, ISolutionProjects projects, IRenames renames, IIgnoredFiles ignored, ReferenceRepair repair) : IPlanner
{
    /// <param name="root">The solution's root, or the folder a new solution is made in.</param>
    /// <param name="current">The solution's manifest; null when there is no solution yet, and the base is empty.</param>
    /// <param name="next">The manifest the solution will have.</param>
    /// <param name="mode">How the files are merged: by git or by the tool, and whether a conflict takes the template's side.</param>
    /// <param name="ct">Cancels the plan.</param>
    /// <param name="step">
    /// A step of an upgrade a part at a time: the base takes the next output of the parts merged before, the target
    /// that and this part; null for the whole upgrade.
    /// </param>
    public async Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, MergeMode mode, CancellationToken ct, PartStep? step = null)
    {
        // The two trees do not depend on each other: generated side by side.
        var baseTree = current is null ? Task.FromResult<string?>(null) : BaseAsync(current, ct);
        var nextTree = staging.BuildAsync(next, withSolutionFile: current is null, ct);
        var baseDir = await baseTree;
        var nextDir = await nextTree;
        // The types the template moves are read from the whole outputs: a part's repair follows the moves of every part.
        var types = baseDir is null ? null : TypeMap.Between(TypeIndex.Read(baseDir), TypeIndex.Read(nextDir));
        IReadOnlyList<string>? parts = null;
        IReadOnlySet<string> waiting = new HashSet<string>();
        if (step is not null && baseDir is not null)
        {
            var moves = types!;
            var before = baseDir;
            parts = step.Parts ?? PartsOf(root, nextDir, moves);
            // A part follows only the moves between the parts merged so far: a type that goes to a part not yet merged keeps its using.
            types = moves.Within(step.InDoneOrThis);
            (baseDir, nextDir) = (Folders.Overlay(baseDir, nextDir, step.InDone), Folders.Overlay(baseDir, nextDir, step.InDoneOrThis));
            // The template's files of the parts not merged yet carry in the solution the repair of the parts before: both
            // sides of the step carry it too, so a later part merges them as the template changes them, with no conflict.
            await repair.BeforeBuildAsync(baseDir, moves.Within(step.InDone), [], ct);
            await repair.BeforeBuildAsync(nextDir, types, [], ct);
            PackageVersions.KeepReferenced(baseDir, before);
            PackageVersions.KeepReferenced(nextDir, before);
            // The repair by the build's errors cannot be carried by the trees: the template's files of the parts not merged
            // yet wait for their part, which brings them repaired.
            waiting = Folders.RelativeFiles(nextDir).Where(p => !step.InDoneOrThis(p)).ToHashSet(StringComparer.Ordinal);
        }

        var packages = PackageVersions.Hold(baseDir, nextDir, next.Packages, TemplateVersions.IsMajorStep(current?.Template, next.Template));
        var byGit = mode.ByGit && current is not null;
        var changes = byGit ? TemplateDiff.Of(baseDir!, nextDir) : await MergeAsync(root, baseDir, nextDir, mode, ct);
        if (current is null)
        {
            return new Plan(root, next, changes, new MembershipChange([], []), packages);
        }

        var membership = await MembershipAsync(root, baseDir!, nextDir, ct);
        return new Plan(root, next, [.. changes.Where(c => !IsSolutionFile(c.Path))], membership, packages, types,
            byGit ? (new TemplateCommit(baseDir!, current!.Template), new TemplateCommit(nextDir, next.Template)) : null, mode.PreferTemplate, Parts: parts, Waiting: waiting);
    }

    /// <summary>
    /// The tool's own merge: the files the template moved are followed, then each file is decided and merged. A file
    /// of the solution its git ignores is left as it is, as the merge by git leaves it.
    /// </summary>
    private async Task<List<Change>> MergeAsync(string root, string? baseDir, string nextDir, MergeMode mode, CancellationToken ct)
    {
        var moved = baseDir is null ? null : await renames.FindAsync(baseDir, nextDir, ct);
        var changes = await merge.ComputeAsync(root, baseDir, nextDir, mode.PreferTemplate, moved, ct);
        var leftAlone = await ignored.InAsync(root, ct);
        return [.. changes.Where(c => !leftAlone.Contains(c.Path))];
    }

    /// <summary>
    /// The parts of an upgrade a part at a time, from the solution's files and the template's next output together: a
    /// project the template adds is a part of its own, in the order of what it references.
    /// </summary>
    private static IReadOnlyList<string> PartsOf(string root, string nextDir, TypeMap moves)
    {
        var solution = Folders.RelativeFiles(root).ToList();
        var next = Folders.RelativeFiles(nextDir).ToList();
        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string part, IEnumerable<string> uses) =>
            (references.TryGetValue(part, out var set) ? set : references[part] = new HashSet<string>(StringComparer.Ordinal)).UnionWith(uses);
        foreach (var (part, uses) in CommonReferences.Read(root, solution).Concat(CommonReferences.Read(nextDir, next)))
        {
            Add(part, uses);
        }

        // A project of Common a type leaves goes after the one it goes to: the type arrives before it leaves.
        foreach (var move in moves.Moves)
        {
            if (UpgradeParts.CommonPartOf(move.From.Project) is { } from && UpgradeParts.CommonPartOf(move.To.Project) is { } to && from != to)
            {
                Add(from, [to]);
            }
        }

        return UpgradeParts.In(solution.Concat(next), references.ToDictionary(r => r.Key, r => (IReadOnlySet<string>)r.Value, StringComparer.Ordinal));
    }

    private async Task<string?> BaseAsync(Manifest current, CancellationToken ct) => await staging.BuildAsync(current, withSolutionFile: false, ct);

    private async Task<MembershipChange> MembershipAsync(string root, string baseDir, string nextDir, CancellationToken ct) =>
        MembershipChange.Of(
            await projects.ListAsync(Layout.SolutionFileOf(root), ct),
            SolutionProjects.InTree(baseDir),
            SolutionProjects.InTree(nextDir));

    /// <summary>All.sln of an existing solution changes by its projects, never by its text.</summary>
    private static bool IsSolutionFile(string path) =>
        path.StartsWith(Layout.ServicesFolder + "/", StringComparison.Ordinal) && path.EndsWith(Layout.SolutionSuffix, StringComparison.Ordinal)
        && !path[(Layout.ServicesFolder.Length + 1)..].Contains('/');
}
