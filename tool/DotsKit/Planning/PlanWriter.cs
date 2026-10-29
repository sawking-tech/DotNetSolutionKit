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
    /// <summary>Writes the plan and returns the files left in conflict.</summary>
    Task<IReadOnlyList<string>> WriteAsync(Plan plan, CancellationToken ct);
}

internal sealed class PlanWriter(ISolutionProjects projects, IGitMerge git) : IPlanWriter
{
    public async Task<IReadOnlyList<string>> WriteAsync(Plan plan, CancellationToken ct)
    {
        var transaction = new FileTransaction(plan.Root);
        IReadOnlyList<string> conflicts;
        if (plan.ByGit is var (@base, next))
            conflicts = await git.MergeAsync(plan.Root, @base, next, plan.PreferTemplate, ct);
        else
        {
            foreach (var change in plan.Changes)
                Add(transaction, change);
            conflicts = [.. plan.Changes.Where(c => c.Kind == ChangeKind.Conflict).Select(c => c.Path)];
        }
        transaction.Write(Layout.ManifestPath, System.Text.Encoding.UTF8.GetBytes(plan.Next.Serialize()));
        if (!plan.Membership.IsEmpty)
            transaction.Guard(SolutionFile(plan.Root));
        if (plan.ByGit is null)
            await transaction.CommitAsync(() => ApplyMembershipAsync(plan, ct));
        else
            await WriteBesideTheMergeAsync(plan, transaction, SolutionFile(plan.Root), ct);
        return conflicts;
    }

    /// <summary>All.sln relative to the root; a solution there already has it, a new one not yet.</summary>
    private static string SolutionFile(string root) => Path.GetRelativePath(root, Layout.SolutionFileOf(root)).Replace('\\', '/');

    /// <summary>
    /// The manifest and All.sln join git's merge, so committing it takes them and <c>git merge --abort</c> puts them
    /// back; a failure here undoes the merge as well as the files.
    /// </summary>
    private async Task WriteBesideTheMergeAsync(Plan plan, FileTransaction transaction, string solutionFile, CancellationToken ct)
    {
        try
        {
            await transaction.CommitAsync(() => ApplyMembershipAsync(plan, ct));
            await git.StageAsync(plan.Root, [Layout.ManifestPath, solutionFile], ct);
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
            transaction.Delete(change.Path);
        else if (change.Content is not null)
            transaction.Write(change.Path, change.Content.ToBytes());
    }

    private async Task ApplyMembershipAsync(Plan plan, CancellationToken ct)
    {
        if (plan.Membership.IsEmpty)
            return;
        var solutionFile = Layout.SolutionFileOf(plan.Root);
        foreach (var project in plan.Membership.Add)
            await projects.AddAsync(solutionFile, project, ct);
        foreach (var project in plan.Membership.Remove)
            await projects.RemoveAsync(solutionFile, project, ct);
    }
}
