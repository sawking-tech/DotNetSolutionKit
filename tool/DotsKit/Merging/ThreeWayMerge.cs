using DotsKit.Io;

namespace DotsKit.Merging;

/// <summary>
/// The changes a solution needs to have what the template gives now, keeping what the solution changed: every
/// file of the base and the new tree goes through <see cref="MergeRule"/>, and a file both sides changed through
/// the text merger. Line endings are merged as LF and written back as the solution's copy has them.
/// </summary>
internal sealed class ThreeWayMerge(ITextMerger merger)
{
    /// <param name="solution">The solution's root, whose files are the team's side.</param>
    /// <param name="baseDir">What the template gave before; null when the solution is new.</param>
    /// <param name="newDir">What the template gives now.</param>
    /// <param name="preferTemplate">--force: in a conflict take the template's side; the solution's lines stay in git history.</param>
    /// <param name="renames">The files the template moved, new path to old (<see cref="IRenames"/>); none for a new solution.</param>
    /// <param name="ct">Cancels the merge before anything is written.</param>
    public async Task<List<Change>> ComputeAsync(string solution, string? baseDir, string newDir, bool preferTemplate,
        IReadOnlyDictionary<string, string>? renames, CancellationToken ct)
    {
        var paths = new SortedSet<string>(Folders.RelativeFiles(newDir), StringComparer.Ordinal);
        if (baseDir is not null)
        {
            paths.UnionWith(Folders.RelativeFiles(baseDir));
        }

        var changes = new List<Change>();
        foreach (var (to, from) in Moves(solution, renames))
        {
            paths.Remove(to);
            paths.Remove(from);
            changes.AddRange(await MoveAsync(solution, baseDir!, newDir, from, to, preferTemplate, ct));
        }
        foreach (var path in paths)
        {
            var decision = MergeRule.Decide(path, FileText.Read(baseDir, path), FileText.Read(newDir, path), FileText.Read(solution, path));
            var change = decision switch
            {
                MergeRule.Final final => final.Change,
                MergeRule.NeedsMerge merge => await MergeAsync(path, merge, preferTemplate, ct),
                _ => throw new InvalidOperationException($"Unknown decision for {path}"),
            };
            if (change is not null)
            {
                changes.Add(change);
            }
        }
        return changes;
    }

    /// <summary>The moves the solution follows: it has the file at the old path and nothing at the new one.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Moves(string solution, IReadOnlyDictionary<string, string>? renames) =>
        (renames ?? new Dictionary<string, string>())
            .Where(r => File.Exists(Path.Combine(solution, r.Value)) && !File.Exists(Path.Combine(solution, r.Key)))
            .OrderBy(r => r.Key, StringComparer.Ordinal);

    /// <summary>
    /// A file the template moved: decided as one file - the old base, the new template, the solution's copy at the old
    /// path - written at the new path, the old path deleted. The team's changes go with the file.
    /// </summary>
    private async Task<IEnumerable<Change>> MoveAsync(string solution, string baseDir, string newDir, string from, string to, bool preferTemplate, CancellationToken ct)
    {
        var own = FileText.Read(solution, from);
        var moved = $"moved from {from} by the template";
        var decision = MergeRule.Decide(to, FileText.Read(baseDir, from), FileText.Read(newDir, to), own);
        var change = decision switch
        {
            MergeRule.Final { Change: null } => new Change(to, ChangeKind.Add, own, moved),
            MergeRule.Final { Change: { } final } => final with { Reason = $"{moved}; {final.Reason}" },
            MergeRule.NeedsMerge merge => await MergeAsync(to, merge with { Reason = $"{moved}; {merge.Reason}" }, preferTemplate, ct),
            _ => throw new InvalidOperationException($"Unknown decision for {to}"),
        };
        // Kept with no content: the file stays where it was, as it is.
        return change is { Kind: ChangeKind.Kept, Content: null }
            ? [change with { Path = from }]
            : [change, new Change(from, ChangeKind.Delete, null, $"moved to {to} by the template")];
    }

    private async Task<Change> MergeAsync(string path, MergeRule.NeedsMerge merge, bool preferTemplate, CancellationToken ct)
    {
        if (merge.Solution.IsBinary || merge.Template.IsBinary)
        {
            return preferTemplate
                ? new(path, ChangeKind.Update, merge.Template, merge.Reason + "; binary, the template's taken")
                : new(path, ChangeKind.Kept, null, merge.Reason + "; binary, kept as it is");
        }

        // The three sides in the template's style: a change of the style alone conflicts with nothing the team wrote.
        var (solution, @base, template) = (TemplateStyle.Of(path, merge.Solution), TemplateStyle.Of(path, merge.Base), TemplateStyle.Of(path, merge.Template));
        var result = await merger.MergeAsync(
            LineEndings.ToLf(solution.Text), LineEndings.ToLf(@base.Text), LineEndings.ToLf(template.Text), preferTemplate, ct);
        if (result.Failure is not null)
        {
            throw new InvalidOperationException($"git could not merge {path}: {result.Failure}. Nothing was written.");
        }

        var content = solution with { Text = LineEndings.Apply(result.Text, LineEndings.Of(solution.Text)) };
        return result switch
        {
            { Conflicts: > 0 } => new(path, ChangeKind.Conflict, content, merge.Reason + $"; {result.Conflicts} conflict(s) marked"),
            { Taken: > 0 } => new(path, ChangeKind.Merge, content, merge.Reason + $"; {result.Taken} conflict(s) take the template's lines, by --force"),
            _ => new(path, ChangeKind.Merge, content, merge.Reason),
        };
    }
}
