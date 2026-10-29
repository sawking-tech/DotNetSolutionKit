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

/// <summary>The files left in conflict, and the repairs before the build that changed a file.</summary>
internal sealed record WriteResult(IReadOnlyList<string> Conflicts, IReadOnlyList<Repair> Repaired)
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
            AddWhatTheBranchLeftOut(plan, transaction);
        }
        else
        {
            foreach (var change in plan.Changes)
            {
                Add(transaction, change);
            }

            conflicts = [.. plan.Changes.Where(c => c.Kind == ChangeKind.Conflict).Select(c => c.Path)];
        }
        transaction.Write(Layout.ManifestPath, System.Text.Encoding.UTF8.GetBytes(plan.Written(conflicts.Count > 0).Serialize()));
        if (!plan.Membership.IsEmpty)
        {
            transaction.Guard(SolutionFile(plan.Root));
        }

        if (plan.ByGit is null)
        {
            await transaction.CommitAsync(() => ApplyMembershipAsync(plan, ct));
            return new(conflicts, await RepairWrittenAsync(plan, conflicts, ct));
        }
        return new(conflicts, await WriteBesideTheMergeAsync(plan, transaction, conflicts, SolutionFile(plan.Root), ct));
    }

    /// <summary>
    /// The usings of namespaces the template gave up, repaired from the text before any build, in every project of
    /// the solution; a part of an upgrade a part at a time repairs by the moves of the parts merged so far.
    /// </summary>
    private async Task<IReadOnlyList<Repair>> RepairAsync(Plan plan, IReadOnlyList<string> conflicts, CancellationToken ct) =>
        plan.Types is null ? [] : await repair.BeforeBuildAsync(plan.Root, plan.Types, conflicts, ct);

    /// <summary>
    /// The repair of the merge by files, which runs once its files are written: a repair that stops, by an error or a
    /// cancel, says that the files are written, as no undo follows it.
    /// </summary>
    private async Task<IReadOnlyList<Repair>> RepairWrittenAsync(Plan plan, IReadOnlyList<string> conflicts, CancellationToken ct)
    {
        try
        {
            return await RepairAsync(plan, conflicts, ct);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            var why = e is OperationCanceledException ? "it was cancelled" : e.Message.Split('\n')[0].Trim();
            throw new InvalidOperationException($"The files are written, and the repair of usings and references stopped: {why}. git diff shows what is written.", e);
        }
    }

    /// <summary>All.sln relative to the root; a solution there already has it, a new one not yet.</summary>
    private static string SolutionFile(string root) => Path.GetRelativePath(root, Layout.SolutionFileOf(root)).Replace('\\', '/');

    /// <summary>
    /// The manifest, All.sln and the files the repair changed join git's merge, so committing it takes them and
    /// <c>git merge --abort</c> puts them back; a failure here undoes the merge as well as the files. Staging and the
    /// repair run inside the transaction, so a failure of either takes back what it wrote - the manifest, All.sln and
    /// the files the branch left out, which git does not know - and the abort takes back the merge. The repair
    /// leaves a file in conflict alone, so staging never marks one resolved.
    /// </summary>
    private async Task<IReadOnlyList<Repair>> WriteBesideTheMergeAsync(Plan plan, FileTransaction transaction, IReadOnlyList<string> conflicts, string solutionFile, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<Repair> repaired = [];
            await transaction.CommitAsync(async () =>
            {
                await ApplyMembershipAsync(plan, ct);
                await git.StageAsync(plan.Root, [Layout.ManifestPath, solutionFile], ct);
                repaired = await RepairAsync(plan, conflicts, ct);
                var written = repaired.Select(r => Path.GetRelativePath(plan.Root, RepairReport.FileOf(r)).Replace('\\', '/')).Distinct().ToList();
                if (written.Count > 0)
                {
                    await git.StageAsync(plan.Root, written, ct);
                }
            });
            return repaired;
        }
        catch (Exception failure)
        {
            await git.AbortKeepingTheCauseAsync(plan.Root, failure);
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

    /// <summary>
    /// A file the template adds that the merge left off the branch (<see cref="IGitMerge.LeftOut"/>): one the
    /// solution's git ignores, as the secrets file of a new service. The merge by files writes it, so the merge by git
    /// does too, beside the merge; a file of that name the team has already stays as it is.
    /// </summary>
    private void AddWhatTheBranchLeftOut(Plan plan, FileTransaction transaction)
    {
        foreach (var added in plan.Changes.Where(c =>
            c.Kind == ChangeKind.Add && git.LeftOut.Contains(c.Path) && !File.Exists(Path.Combine(plan.Root, c.Path))))
        {
            Add(transaction, added);
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
