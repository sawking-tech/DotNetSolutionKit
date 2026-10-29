using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.Solutions;

namespace DotsKit.Planning;

/// <summary>
/// Writes a plan: the files - by the tool, or by a git merge of the template's output - then the manifest and the
/// projects of All.sln in one transaction, which all land or none does.
/// </summary>
internal interface IPlanWriter
{
    /// <summary>Writes the plan, repairs what the text shows of the types the template moved, and says what is left.</summary>
    Task<WriteResult> WriteAsync(Plan plan, CancellationToken ct);
}

/// <summary>The files left in conflict, and the files the repair before the build changed.</summary>
internal sealed record WriteResult(IReadOnlyList<string> Conflicts, IReadOnlyList<string> Repaired)
{
    public static readonly WriteResult Nothing = new([], []);
}

internal sealed class PlanWriter(ISolutionProjects projects, IGitMerge git, ReferenceRepair repair) : IPlanWriter
{
    public async Task<WriteResult> WriteAsync(Plan plan, CancellationToken ct)
    {
        var transaction = new FileTransaction(plan.Root);
        IReadOnlyList<string> conflicts;
        if (plan.ByGit is var (@base, next))
        {
            conflicts = await git.MergeAsync(plan.Root, @base, next, plan.PreferTemplate, ct);
        }
        else
        {
            foreach (var change in plan.Changes)
            {
                Add(transaction, change);
            }

            conflicts = [.. plan.Changes.Where(c => c.Kind == ChangeKind.Conflict).Select(c => c.Path)];
        }
        transaction.Write(Layout.ManifestPath, System.Text.Encoding.UTF8.GetBytes(plan.Next.Serialize()));
        if (!plan.Membership.IsEmpty)
        {
            transaction.Guard(SolutionFile(plan.Root));
        }

        if (plan.ByGit is null)
        {
            await transaction.CommitAsync(() => ApplyMembershipAsync(plan, ct));
            return new(conflicts, await RepairAsync(plan, conflicts, ct));
        }
        return new(conflicts, await WriteBesideTheMergeAsync(plan, transaction, conflicts, SolutionFile(plan.Root), ct));
    }

    /// <summary>The usings of namespaces the template gave up, repaired from the text before any build.</summary>
    private async Task<IReadOnlyList<string>> RepairAsync(Plan plan, IReadOnlyList<string> conflicts, CancellationToken ct) =>
        plan.Types is null ? [] : await repair.BeforeBuildAsync(plan.Root, plan.Types, conflicts, ct);

    /// <summary>All.sln relative to the root; a solution there already has it, a new one not yet.</summary>
    private static string SolutionFile(string root) => Path.GetRelativePath(root, Layout.SolutionFileOf(root)).Replace('\\', '/');

    /// <summary>
    /// The manifest, All.sln and the files the repair changed join git's merge, so committing it takes them and
    /// <c>git merge --abort</c> puts them back; a failure here undoes the merge as well as the files. The repair
    /// leaves a file in conflict alone, so staging never marks one resolved.
    /// </summary>
    private async Task<IReadOnlyList<string>> WriteBesideTheMergeAsync(Plan plan, FileTransaction transaction, IReadOnlyList<string> conflicts, string solutionFile, CancellationToken ct)
    {
        try
        {
            await transaction.CommitAsync(() => ApplyMembershipAsync(plan, ct));
            var repaired = await RepairAsync(plan, conflicts, ct);
            var written = repaired.Select(f => Path.GetRelativePath(plan.Root, f).Replace('\\', '/'));
            await git.StageAsync(plan.Root, [Layout.ManifestPath, solutionFile, .. written.Distinct()], ct);
            return repaired;
        }
        catch
        {
            await git.AbortAsync(plan.Root, CancellationToken.None);
            throw;
        }
    }

    /// <summary>A conflict with no content - a file one side removed and the other changed - stays as it is.</summary>
    private static void Add(FileTransaction transaction, Change change)
    {
        if (change.Kind == ChangeKind.Delete)
        {
            transaction.Delete(change.Path);
        }
        else if (change.Content is not null)
        {
            transaction.Write(change.Path, change.Content.ToBytes());
        }
    }

    private async Task ApplyMembershipAsync(Plan plan, CancellationToken ct)
    {
        if (plan.Membership.IsEmpty)
        {
            return;
        }

        var solutionFile = Layout.SolutionFileOf(plan.Root);
        await projects.AddAsync(solutionFile, plan.Membership.Add, ct);
        await projects.RemoveAsync(solutionFile, plan.Membership.Remove, ct);
    }
}
