using DotsKit.Generation;
using DotsKit.Ignoring;
using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.PackageUpdates;
using DotsKit.Solutions;

namespace DotsKit.Planning;

/// <summary>
/// Builds a plan: what the template gave the solution (the base, from the current manifest), what it gives now
/// (from the next one), and git's merge of the two with the solution's own files. The solution's package
/// policy is put into the template's side first, and the files the template moved are followed.
/// </summary>
internal interface IPlanner
{
    Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, MergeMode mode, CancellationToken ct, PartStep? step = null);
}

internal sealed class Planner(IStaging staging, ISolutionProjects projects, IRenames renames, IIgnoredFiles ignored, ReferenceRepair repair,
    IIgnoreRules rules) : IPlanner
{
    /// <param name="root">The solution's root, or the folder a new solution is made in.</param>
    /// <param name="current">The solution's manifest; null when there is no solution yet, and the base is empty.</param>
    /// <param name="next">The manifest the solution will have.</param>
    /// <param name="mode">How git merges: whether a conflict takes the template's side, and how a conflict is marked.</param>
    /// <param name="ct">Cancels the plan.</param>
    /// <param name="step">
    /// A step of an upgrade a part at a time: the base takes the next output of the parts merged before, the target
    /// that and this part; null for the whole upgrade.
    /// </param>
    public async Task<Plan> BuildAsync(string root, Manifest? current, Manifest next, MergeMode mode, CancellationToken ct, PartStep? step = null)
    {
        // The two trees do not depend on each other: generated side by side.
        var baseDir = current is null ? null : await BaseAsync(current, ct);
        var nextDir = await staging.BuildAsync(next, withSolutionFile: current is null, ct);
        // The rules of .dotskitignore hold the paths a person took out of the template's reach: the next output takes
        // them from the base, so the merge finds nothing of the template there. Read before any move, so a held file
        // is no move either.
        var ignore = await IgnoredAsync(root, current, next, nextDir, baseDir, ct);
        // The types the template moves are read from the whole outputs: a part's repair follows the moves of every part.
        var types = baseDir is null ? null : TypeMap.Between(TypeIndex.Read(baseDir), TypeIndex.Read(nextDir));
        IReadOnlyList<string>? parts = null;
        IReadOnlySet<string> waiting = new HashSet<string>();
        if (step is not null && baseDir is not null)
        {
            var moves = types!;
            var before = baseDir;
            parts = step.Parts ?? PartsOf(root, nextDir, moves, await ignored.InAsync(root, ct));
            // The first run decides the parts here: its step reads a path's part from them, as every later run does. The
            // files the template moves are read from the whole outputs, before a step cuts a move in two.
            var movedTo = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (to, from) in await renames.FindAsync(baseDir, nextDir, ct))
            {
                movedTo.TryAdd(from, to);
            }

            step = step with { Parts = parts, MovedTo = movedTo };
            // A part follows only the moves between the parts merged so far: a type that goes to a part not yet merged keeps its using.
            types = moves.Within(step.InDoneOrThis);
            (baseDir, nextDir) = (Folders.Overlay(baseDir, nextDir, step.InDone), Folders.Overlay(baseDir, nextDir, step.InDoneOrThis));
            // The repair of the trees covers the files of the parts merged so far and this one: a file of a part not
            // merged yet is repaired on the solution's side by the build's errors and by its own part later, and the
            // two sides of a merge never carry two repairs of one file. A file the rules hold takes the base's text
            // back after the repair, since the repair writes the text of a held file as it does any other.
            await repair.BeforeBuildAsync(baseDir, moves.Within(step.InDone), [], ct, step.InDone);
            await repair.BeforeBuildAsync(nextDir, types, [], ct, step.InDoneOrThis, ignore.Held);
            HeldFiles.Apply(nextDir, baseDir, ignore.Held);
            PackageVersions.KeepReferenced(baseDir, before, root);
            PackageVersions.KeepReferenced(nextDir, before, root);
            // The repair by the build's errors cannot be carried by the trees: the template's files of the parts not merged
            // yet wait for their part, which brings them repaired.
            waiting = Folders.RelativeFiles(nextDir).Where(p => !step.InDoneOrThis(p)).ToHashSet(StringComparer.Ordinal);
        }
        else if (baseDir is not null)
        {
            // All at once: a package the template drops keeps its version while a project of the team's references it.
            PackageVersions.KeepReferenced(nextDir, baseDir, root);
        }

        var packages = PackageVersions.Hold(baseDir, nextDir, next.Packages, TemplateVersions.IsMajorStep(current?.Template, next.Template), ignore.Held);
        // A new solution in an empty folder is unpacked, not merged. Beside files it merges by git, like an upgrade:
        // an empty tree then stands for what the template gave before.
        var byGit = current is not null || !SolutionRoot.IsEmpty(root);
        var baseTree = baseDir ?? (byGit ? Folders.NewTemp("empty-base") : null);
        IReadOnlyList<Change> changes;
        if (byGit)
        {
            changes = TemplateDiff.Of(baseTree, nextDir);
        }
        else
        {
            var leftAlone = await ignored.InAsync(root, ct);
            changes = [.. TemplateDiff.Of(null, nextDir).Where(c => !leftAlone.Contains(c.Path))];
        }
        if (current is null)
        {
            return new Plan(root, next, changes, new MembershipChange([], []), packages,
                ByGit: byGit ? (new TemplateCommit(baseTree!, "a new solution"), new TemplateCommit(nextDir, next.Template)) : null,
                PreferTemplate: mode.PreferTemplate, MarkBase: mode.MarkBase, NextTree: nextDir, Ignore: ignore);
        }

        var membership = await MembershipAsync(root, baseDir!, nextDir, ct);
        return new Plan(root, next, [.. changes.Where(c => !IsSolutionFile(c.Path))], membership, packages, types,
            (new TemplateCommit(baseDir!, current!.Template), new TemplateCommit(nextDir, next.Template)), mode.PreferTemplate, mode.MarkBase,
            Parts: parts, Waiting: waiting, BaseTree: baseDir, NextTree: nextDir, Ignore: ignore);
    }

    /// <summary>
    /// The parts of an upgrade a part at a time, from the solution's files and the template's next output together: a
    /// project the template adds is a part of its own, in the order of what it references.
    /// </summary>
    private static IReadOnlyList<string> PartsOf(string root, string nextDir, TypeMap moves, IReadOnlySet<string> leftAlone)
    {
        var solution = Folders.RelativeFiles(root).Where(f => !leftAlone.Contains(f)).ToList();
        var next = Folders.RelativeFiles(nextDir).ToList();
        var references = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string part, IEnumerable<string> uses) =>
            (references.TryGetValue(part, out var set) ? set : references[part] = new HashSet<string>(StringComparer.Ordinal)).UnionWith(uses);
        foreach (var (part, uses) in CommonReferences.Read(root, solution).Concat(CommonReferences.Read(nextDir, next)))
        {
            Add(part, uses);
        }

        // A project a type leaves goes after the one it goes to - a project of Common or the capabilities: the type arrives before it leaves.
        foreach (var move in moves.Moves)
        {
            if (UpgradeParts.OrderablePartOf(move.From.Project) is { } from && UpgradeParts.OrderablePartOf(move.To.Project) is { } to && from != to)
            {
                Add(from, [to]);
            }
        }

        return UpgradeParts.In(solution.Concat(next), references.ToDictionary(r => r.Key, r => (IReadOnlySet<string>)r.Value, StringComparer.Ordinal));
    }

    private async Task<string?> BaseAsync(Manifest current, CancellationToken ct) => await staging.BuildAsync(current, withSolutionFile: false, ct);

    /// <summary>The line a pin is read and renewed by: the release an upgrade goes to, or the tool's own for the sources.</summary>
    private static string LineOf(string version) => version == TemplateSource.SourcesVersion ? ToolVersion.Current : version;

    /// <summary>
    /// The rules of <c>.dotskitignore</c> read at the version the solution goes to, and their effect on the template's
    /// next output: the held paths take the base's text, and a held path the template adds leaves the output. The
    /// decision goes with the plan, so the report shows what a rule held and which pins the upgrade spent.
    /// </summary>
    private async Task<IgnoreDecision> IgnoredAsync(string root, Manifest? current, Manifest next, string nextDir, string? baseDir, CancellationToken ct)
    {
        if (current is null)
        {
            return IgnoreDecision.None;
        }

        // Every path the upgrade could write: the solution's own, the template's next output, and the base's, so a held
        // path the template removes is held too.
        IReadOnlyList<string> candidates = [.. Folders.RelativeFiles(root)
            .Concat(Folders.RelativeFiles(nextDir))
            .Concat(baseDir is null ? [] : Folders.RelativeFiles(baseDir))
            .Distinct(StringComparer.Ordinal)];
        // The sources carry no version of their own: a pin is read and renewed by the tool's, which is the line they
        // become. The solution's release stands as the one it came from, so a pin of the major is read against it.
        var decision = await rules.DecideAsync(root, LineOf(next.Template), LineOf(current.Template), candidates, ct);
        HeldFiles.Apply(nextDir, baseDir, decision.Held);
        return decision;
    }

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
