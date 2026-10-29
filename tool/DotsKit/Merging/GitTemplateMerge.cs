using DotsKit.Io;

namespace DotsKit.Merging;

/// <summary>Merges the template's output into a solution by git, leaving the merge for a person to commit.</summary>
internal interface IGitMerge
{
    /// <summary>
    /// Brings the <paramref name="next"/> output into the working branch over the <paramref name="base"/> one, and
    /// returns the files left in conflict.
    /// </summary>
    Task<IReadOnlyList<string>> MergeAsync(string root, TemplateCommit @base, TemplateCommit next, bool preferTemplate, CancellationToken ct);

    /// <summary>Adds the files the tool wrote beside the merge to it, so <c>git merge --abort</c> puts them back too.</summary>
    Task StageAsync(string root, IReadOnlyList<string> paths, CancellationToken ct);

    /// <summary>Undoes a merge this run started: a later step of the command failed.</summary>
    Task AbortAsync(string root, CancellationToken ct);
}

/// <summary>A tree the template generated and the version it is of.</summary>
internal sealed record TemplateCommit(string Tree, string Version);

/// <summary>
/// The branch dotskit/template holds what the template gave the solution, a commit per output. Every run goes the
/// same way: git knows the base when the branch's tip has the base's tree and the working branch has the tip in its
/// history; otherwise the base is committed - the branch's first commit has no parent - and merged with
/// <c>-s ours</c>, which keeps the solution's files and tells git the base. Then the next output is committed on top
/// and merged with <c>--no-commit</c>: git keeps the team's changes, follows the files the template moved, marks a
/// conflict for the IDE, and <c>git merge --abort</c> undoes it.
/// The commits are made with git's plumbing and an index of their own: nothing is checked out, no hook runs. What
/// the solution's git ignores, a local secret among them, and All.sln, which changes by <c>dotnet sln</c>, are never
/// in the branch.
/// </summary>
internal sealed class GitTemplateMerge(IProcessRunner runner) : IGitMerge
{
    public const string Branch = "dotskit/template";

    private const string BranchRef = "refs/heads/" + Branch;
    private const string IndexVariable = "GIT_INDEX_FILE";

    public async Task<IReadOnlyList<string>> MergeAsync(string root, TemplateCommit @base, TemplateCommit next, bool preferTemplate, CancellationToken ct)
    {
        await RefuseAMergeInProgressAsync(root, ct);
        var tip = await TipAsync(root, ct);
        var baseCommit = await KnownBaseAsync(root, tip, @base, ct);
        var nextCommit = await CommitAsync(root, next.Tree, baseCommit, $"dotskit: the template {next.Version}", ct);
        // Moved only from the tip read above: a commit another run put there in between is not lost (empty: no branch yet).
        await runner.RunCheckedAsync("git", ["update-ref", BranchRef, nextCommit, tip ?? ""], root, ct);
        var merge = await runner.RunAsync("git", ["merge", "--no-ff", "--no-commit", .. preferTemplate ? new[] { "-X", "theirs" } : [], Branch], root, ct);
        var conflicts = await PathsAsync(root, ["diff", "--name-only", "-z", "--diff-filter=U"], null, ct);
        if (merge.Code != 0 && conflicts.Count == 0)
            throw new InvalidOperationException($"git could not merge {Branch}: {FirstLine(merge)} Nothing is merged; run git merge --abort if git started one.");
        return conflicts;
    }

    public Task StageAsync(string root, IReadOnlyList<string> paths, CancellationToken ct) =>
        runner.RunCheckedAsync("git", ["add", "--", .. paths], root, ct);

    public Task AbortAsync(string root, CancellationToken ct) => runner.RunAsync("git", ["merge", "--abort"], root, ct);

    private async Task RefuseAMergeInProgressAsync(string root, CancellationToken ct)
    {
        if ((await runner.RunAsync("git", ["rev-parse", "-q", "--verify", "MERGE_HEAD"], root, ct)).Code == 0)
            throw new InvalidOperationException("A git merge is in progress: commit it or run git merge --abort, then run the command again.");
    }

    /// <summary>
    /// The commit git merges over: the branch's tip when it is the base and the working branch has it; otherwise the
    /// base, committed on the tip (or as the first commit) and merged keeping the solution's files.
    /// </summary>
    private async Task<string> KnownBaseAsync(string root, string? tip, TemplateCommit @base, CancellationToken ct)
    {
        var tree = await TreeAsync(root, @base.Tree, ct);
        if (tip is not null && await TreeOfAsync(root, tip, ct) == tree && await IsInHistoryAsync(root, tip, ct))
            return tip;
        var commit = await CommitTreeAsync(root, tree, tip, $"dotskit: the template {@base.Version}, the base of the solution", ct);
        await runner.RunCheckedAsync("git", ["merge", "-s", "ours", "--no-ff", "--allow-unrelated-histories", "--no-edit",
            "-m", $"dotskit: the solution is of the template {@base.Version}", commit], root, ct);
        return commit;
    }

    private async Task<string> CommitAsync(string root, string folder, string parent, string message, CancellationToken ct) =>
        await CommitTreeAsync(root, await TreeAsync(root, folder, ct), parent, message, ct);

    private async Task<string> CommitTreeAsync(string root, string tree, string? parent, string message, CancellationToken ct) =>
        (await runner.RunCheckedAsync("git", ["commit-tree", tree, .. parent is null ? [] : new[] { "-p", parent }, "-m", message], root, ct)).Trim();

    /// <summary>
    /// The tree of a folder the template generated, written by an index of its own: the solution's own index and
    /// files are not touched. A path the solution's git ignores, by any of its rules, and All.sln are left out.
    /// </summary>
    private async Task<string> TreeAsync(string root, string folder, CancellationToken ct)
    {
        var work = Folders.NewTemp("template-tree");
        var index = new Dictionary<string, string> { [IndexVariable] = Path.Combine(work, "index") };
        var gitDir = (await runner.RunCheckedAsync("git", ["rev-parse", "--absolute-git-dir"], root, ct)).Trim();
        await runner.RunCheckedAsync("git", ["--git-dir", gitDir, "--work-tree", folder, "add", "-A", "--", "."], folder, ct, index);
        var ignored = await PathsAsync(root, ["ls-files", "-z", "--cached", "--ignored", "--exclude-standard"], index, ct);
        var left = (await PathsAsync(root, ["ls-files", "-z"], index, ct)).Where(IsNotTheTemplates).Concat(ignored).Distinct().ToList();
        if (left.Count > 0)
        {
            var list = Path.Combine(work, "left-out");
            await File.WriteAllTextAsync(list, string.Join('\0', left), ct);
            // -f: the index is this run's own, and git would otherwise compare it with the solution's files.
            await runner.RunCheckedAsync("git", ["rm", "--cached", "-q", "-f", "--pathspec-from-file", list, "--pathspec-file-nul"], root, ct, index);
        }
        return (await runner.RunCheckedAsync("git", ["write-tree"], root, ct, index)).Trim();
    }

    private async Task<string?> TipAsync(string root, CancellationToken ct)
    {
        var tip = await runner.RunAsync("git", ["rev-parse", "-q", "--verify", BranchRef], root, ct);
        return tip.Code == 0 ? tip.Output.Trim() : null;
    }

    private async Task<string> TreeOfAsync(string root, string commit, CancellationToken ct) =>
        (await runner.RunCheckedAsync("git", ["rev-parse", commit + "^{tree}"], root, ct)).Trim();

    private async Task<bool> IsInHistoryAsync(string root, string commit, CancellationToken ct) =>
        (await runner.RunAsync("git", ["merge-base", "--is-ancestor", commit, "HEAD"], root, ct)).Code == 0;

    /// <summary>All.sln, and what the tool never counts as the template's files: build output, the manifest.</summary>
    private static bool IsNotTheTemplates(string path) =>
        (path.StartsWith(Layout.ServicesFolder + "/", StringComparison.Ordinal) && path.EndsWith(Layout.SolutionSuffix, StringComparison.Ordinal))
        || path.Split('/').Any(Layout.Ignored.Contains);

    private async Task<IReadOnlyList<string>> PathsAsync(string root, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? environment,
        CancellationToken ct) =>
        (await runner.RunCheckedAsync("git", args, root, ct, environment)).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static string FirstLine(ProcessResult result) =>
        (string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim().Split('\n')[0];
}
