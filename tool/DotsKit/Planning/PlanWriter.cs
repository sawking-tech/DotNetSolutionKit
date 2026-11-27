using DotsKit.Ignoring;
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

/// <summary>The files left in conflict, the files --force took the template's side in, the repairs before the build that changed a file, and what the review of .dotskitignore says.</summary>
internal sealed record WriteResult(IReadOnlyList<string> Conflicts, IReadOnlyList<Repair> Repaired, IReadOnlyList<string> Taken, IReadOnlyList<string> Pins)
{
    public WriteResult(IReadOnlyList<string> conflicts, IReadOnlyList<Repair> repaired, IReadOnlyList<string> taken)
        : this(conflicts, repaired, taken, [])
    {
    }

    public static readonly WriteResult Nothing = new([], [], [], []);
}

internal sealed class PlanWriter(ISolutionProjects projects, IGitMerge git, ReferenceRepair repair) : IPlanWriter
{
    public async Task<WriteResult> WriteAsync(Plan plan, CancellationToken ct)
    {
        var transaction = new FileTransaction(plan.Root);
        IReadOnlyList<string> conflicts;
        if (plan.ByGit is var (@base, next))
        {
            conflicts = await git.MergeAsync(plan.Root, @base, next, plan.PreferTemplate, plan.MarkBase, ct);
            AddWhatTheBranchLeftOut(plan, transaction);
        }
        else
        {
            foreach (var change in plan.Changes)
            {
                Add(transaction, change);
            }

            // The tool's own write never merges: nothing it writes can stay in conflict.
            conflicts = [];
        }
        transaction.Write(Layout.ManifestPath, System.Text.Encoding.UTF8.GetBytes(plan.Written(conflicts.Count > 0).Serialize()));
        // A pin the upgrade spends is written back beside the merge, with the lines the team wrote around it: the
        // team sees the change in the review, git commits it with the part, and git merge --abort puts it back too.
        // Every file of the rules takes its own share of the spent pins, wherever in the tree it sits.
        IReadOnlyList<string> pins = [];
        List<string> reviewed = [];
        foreach (var file in DotskitIgnores.Load(plan.Root))
        {
            if (PinReview.Of(file, Spent(plan), plan.Ignore?.Version ?? plan.Next.Template) is not { Lines.Count: > 0 } review)
            {
                continue;
            }

            transaction.Write(file.PathIn(plan.Root), System.Text.Encoding.UTF8.GetBytes(review.Text));
            reviewed.Add(file.PathIn(plan.Root));
            pins = [.. pins, .. review.Lines];
        }

        if (!plan.Membership.IsEmpty)
        {
            transaction.Guard(SolutionFile(plan.Root));
        }

        if (plan.ByGit is null)
        {
            await transaction.CommitAsync(() => ApplyMembershipAsync(plan, ct));
            return new(conflicts, await RepairWrittenAsync(plan, conflicts, ct), [], pins);
        }
        return new(conflicts, await WriteBesideTheMergeAsync(plan, transaction, reviewed, conflicts, SolutionFile(plan.Root), ct), git.Taken, pins);
    }
    /// <summary>
    /// The usings of namespaces the template gave up, repaired from the text before any build, in every project of
    /// the solution; a part of an upgrade a part at a time repairs by the moves of the parts merged so far.
    /// </summary>
    private async Task<IReadOnlyList<Repair>> RepairAsync(Plan plan, IReadOnlyList<string> conflicts, CancellationToken ct)
    {
        // A file the rules hold is no file to repair, whatever the shape of the repair: the upgrade writes no line of
        // it, a using of a moved type and a project's reference alike. The template's frame first: the references
        // between its projects as its next version has them.
        var held = plan.Ignore?.Held ?? new HashSet<string>();
        var left = conflicts.Concat(held).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<Repair> frame = plan.NextTree is null || plan.BaseTree is null
            ? []
            : await repair.ApplyAsync([.. TemplateReferences.Decide(plan.Root, plan.BaseTree, plan.NextTree, left), .. PackageUpdates.CentralVersions.Decide(plan.Root, left)], ct);
        return plan.Types is null ? frame : [.. frame, .. await repair.BeforeBuildAsync(plan.Root, plan.Types, conflicts, ct, held: held)];
    }

    /// <summary>
    /// The repair of a write the tool made itself, which runs once its files are written: a repair that stops, by an error or a
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
    /// The spent pins with whether the solution still differs from the template there, once the merge is written: a file
    /// the team edited differs and keeps its rule for another round; one the merge brought to the template's own text
    /// matches it, and its rule goes. The paths are the ones git itself read the rule to name.
    /// </summary>
    private static IReadOnlyList<(IgnoreRule Rule, bool Difference)> Spent(Plan plan) =>
        plan.Ignore is not { Spent.Count: > 0 } ignore || plan.NextTree is null
            ? []
            : [.. ignore.Spent.Select(rule => (rule, StaysDifferent(rule, plan, ignore)))];

    private static bool StaysDifferent(IgnoreRule rule, Plan plan, IgnoreDecision ignore)
    {
        foreach (var path in ignore.PathsOf.GetValueOrDefault(rule) ?? new HashSet<string>())
        {
            var template = Path.Combine(plan.NextTree!, path);
            var solution = Path.Combine(plan.Root, path);
            if (!File.Exists(solution))
            {
                continue;
            }

            if (!File.Exists(template) || !Same(File.ReadAllText(template), File.ReadAllText(solution)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Two texts equal, whether written on two OSes: the line ends of each are read as one.</summary>
    private static bool Same(string left, string right) =>
        left.ReplaceLineEndings("\n").Equals(right.ReplaceLineEndings("\n"), StringComparison.Ordinal);

    /// <summary>
    /// The manifest, All.sln and the files the repair changed join git's merge, so committing it takes them and
    /// <c>git merge --abort</c> puts them back; a failure here undoes the merge as well as the files. Staging and the
    /// repair run inside the transaction, so a failure of either takes back what it wrote - the manifest, All.sln and
    /// the files the branch left out, which git does not know - and the abort takes back the merge. The repair
    /// leaves a file in conflict alone, so staging never marks one resolved.
    /// </summary>
    private async Task<IReadOnlyList<Repair>> WriteBesideTheMergeAsync(Plan plan, FileTransaction transaction, List<string> reviewed, IReadOnlyList<string> conflicts, string solutionFile, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<Repair> repaired = [];
            await transaction.CommitAsync(async () =>
            {
                await ApplyMembershipAsync(plan, ct);
                await git.StageAsync(plan.Root, [Layout.ManifestPath, solutionFile], ct);
                // A file the team may have put into its own .gitignore: a merge commit takes it whole, as the team's own
                // file, so it is staged whether the solution's rules hide it or not.
                if (reviewed.Count > 0)
                {
                    await git.StageAsync(plan.Root, reviewed, ct, force: true);
                }
                repaired = await RepairAsync(plan, conflicts, ct);
                var written = repaired.Select(RepairReport.FileOf).Concat(repaired.OfType<CentralizeVersion>().Select(c => c.Central))
                    .Select(f => Path.GetRelativePath(plan.Root, f).Replace('\\', '/')).Distinct().ToList();
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

    /// <summary>A deletion carries no content; everything else the plan carries is written as it is.</summary>
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
    /// solution's git ignores, as the secrets file of a new service. The tool writes it beside the merge; a file
    /// of that name the team has already stays as it is.
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
