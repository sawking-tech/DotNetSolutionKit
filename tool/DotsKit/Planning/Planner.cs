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
    Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, MergeMode mode, CancellationToken ct);
}

internal sealed class Planner(IStaging staging, ThreeWayMerge merge, ISolutionProjects projects, IRenames renames) : IPlanner
{
    /// <param name="current">The solution's manifest; null when there is no solution yet, and the base is empty.</param>
    public async Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, MergeMode mode, CancellationToken ct)
    {
        var baseDir = current is null ? null : await staging.BuildAsync(current, ct);
        var nextDir = await staging.BuildAsync(next, ct);
        var packages = PackageVersions.Hold(baseDir, nextDir, next.Packages, TemplateVersions.IsMajorStep(current?.Template, next.Template));
        var byGit = mode.ByGit && current is not null;
        var changes = byGit ? TemplateDiff.Of(baseDir!, nextDir) : await MergeAsync(root, baseDir, nextDir, mode, ct);
        if (current is null)
            return new Plan(root, next, changes, new MembershipChange([], []), packages);
        var membership = await MembershipAsync(root, baseDir!, nextDir, ct);
        var types = TypeMap.Between(TypeIndex.Read(baseDir!), TypeIndex.Read(nextDir));
        return new Plan(root, next, [.. changes.Where(c => !IsSolutionFile(c.Path))], membership, packages, types,
            byGit ? (new TemplateCommit(baseDir!, current!.Template), new TemplateCommit(nextDir, next.Template)) : null, mode.PreferTemplate);
    }

    /// <summary>The tool's own merge: the files the template moved are followed, then each file is decided and merged.</summary>
    private async Task<List<Change>> MergeAsync(string root, string? baseDir, string nextDir, MergeMode mode, CancellationToken ct)
    {
        var moved = baseDir is null ? null : await renames.FindAsync(baseDir, nextDir, ct);
        return await merge.ComputeAsync(root, baseDir, nextDir, mode.PreferTemplate, moved, ct);
    }

    private async Task<MembershipChange> MembershipAsync(string root, string baseDir, string nextDir, CancellationToken ct) =>
        MembershipChange.Of(
            await projects.ListAsync(Layout.SolutionFileOf(root), ct),
            await projects.ListAsync(Layout.SolutionFileOf(baseDir), ct),
            await projects.ListAsync(Layout.SolutionFileOf(nextDir), ct));

    /// <summary>All.sln of an existing solution changes by its projects, never by its text.</summary>
    private static bool IsSolutionFile(string path) =>
        path.StartsWith(Layout.ServicesFolder + "/", StringComparison.Ordinal) && path.EndsWith(Layout.SolutionSuffix, StringComparison.Ordinal)
        && !path[(Layout.ServicesFolder.Length + 1)..].Contains('/');
}
