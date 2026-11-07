namespace DotsKit.Io;

/// <summary>The git working tree of a solution.</summary>
internal interface IGit
{
    /// <summary>Whether the tree has no uncommitted change: what the tool writes can then be seen and undone in git.</summary>
    Task<bool> IsCleanAsync(string root, CancellationToken ct);

    /// <summary>
    /// The folder of <paramref name="root"/> inside its repository, with a slash at the end; empty at the root or
    /// outside git.
    /// </summary>
    Task<string> PrefixAsync(string root, CancellationToken ct);

    /// <summary>The commit HEAD is at when the tree is clean; null when it is not, or outside git.</summary>
    Task<string?> CleanHeadAsync(string root, CancellationToken ct);

    /// <summary>
    /// The files marked assume-unchanged: git status does not show their local changes, and a reset takes them away.
    /// None outside git.
    /// </summary>
    Task<IReadOnlyList<string>> HiddenChangesAsync(string root, CancellationToken ct);

    /// <summary>Whether the folder is inside a git repository: outside one there is nothing to show or undo a write with.</summary>
    Task<bool> IsRepositoryAsync(string root, CancellationToken ct);

    /// <summary>
    /// --allow-dirty: the uncommitted changes, untracked files among them, committed by plumbing on top of HEAD - no
    /// hook, no checkout - so a merge can run on them and they take part in it as any committed side. Null when the
    /// tree is clean; the commit's parent is where <c>git reset --mixed</c> puts the changes back uncommitted.
    /// </summary>
    Task<(string Commit, string Previous)?> CommitUncommittedAsync(string root, string message, CancellationToken ct);

    /// <summary>--allow-dirty, undone: the branch back at <paramref name="commit"/>, the changes it held uncommitted again.</summary>
    Task UncommitAsync(string root, string commit, CancellationToken ct);

    /// <summary>
    /// Commits what the tree holds - the merge and the manifest a step of an upgrade wrote - by plumbing on top of
    /// HEAD: no hook, no checkout, the team's identity kept where git knows it. A continuous upgrade takes the next
    /// part over this commit.
    /// </summary>
    Task CommitAsync(string root, string message, CancellationToken ct);

    /// <summary>Whether a merge is open: its undo keeps uncommitted changes committed, and they are not put back.</summary>
    Task<bool> MergeInProgressAsync(string root, CancellationToken ct);
}

/// <summary>
/// <c>git status --porcelain</c>, <c>git rev-parse --show-prefix</c> and <c>git ls-files -v</c>. Every git command here is listed in docs/getting-started/tool.md, "What dotskit
/// runs in git": a command added or changed here goes there too.
/// </summary>
internal sealed class Git(IProcessRunner runner) : IGit
{
    /// <summary>The author of a mechanical commit when git knows no identity, as on a fresh runner.</summary>
    internal const string FallbackUserName = "dotskit";

    /// <summary>The address of <see cref="FallbackUserName"/>: the tool's own, no person's.</summary>
    internal const string FallbackUserEmail = "dotskit@sawking.tech";

    /// <summary>
    /// The -c user.name/-c user.email for a plumbing commit: none for the half git already knows, so the team's
    /// identity is kept; the tool's own for the half it does not, so the commit is not refused. An empty value
    /// counts as unknown: git would refuse the commit with it, the same as with none.
    /// </summary>
    internal static async Task<string[]> IdentityArgsAsync(IProcessRunner runner, string root, CancellationToken ct)
    {
        var name = await runner.RunAsync("git", ["config", "user.name"], root, ct);
        var email = await runner.RunAsync("git", ["config", "user.email"], root, ct);
        List<string>? args = null;
        if (name.Code != 0 || string.IsNullOrWhiteSpace(name.Output))
        {
            args = ["-c", $"user.name={FallbackUserName}"];
        }

        if (email.Code != 0 || string.IsNullOrWhiteSpace(email.Output))
        {
            (args ??= []).AddRange(["-c", $"user.email={FallbackUserEmail}"]);
        }

        return args is null ? [] : [.. args];
    }

    public async Task<bool> IsCleanAsync(string root, CancellationToken ct)
    {
        var status = await runner.RunAsync("git", ["status", "--porcelain"], root, ct);
        // Not a repository: nothing to undo the tool's changes with, so the tree is not clean.
        return status.Code == 0 && status.Output.Trim().Length == 0;
    }

    public async Task<string?> CleanHeadAsync(string root, CancellationToken ct)
    {
        if (!await IsCleanAsync(root, ct))
        {
            return null;
        }

        var head = await runner.RunAsync("git", ["rev-parse", "HEAD"], root, ct);
        return head.Code == 0 ? head.Output.Trim() : null;
    }

    public async Task<IReadOnlyList<string>> HiddenChangesAsync(string root, CancellationToken ct)
    {
        var files = await runner.RunAsync("git", ["ls-files", "-v", "-z"], root, ct);
        // git ls-files -v tags a cached file H; assume-unchanged makes the letter lower case, and skip-worktree
        // is S or, with both bits, s. Either way git hides the file's local changes and a checkout or a merge
        // writes over them.
        return files.Code != 0 ? [] : [.. files.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => entry.Length > TagLength && entry[1] == ' ' && (char.IsLower(entry[0]) || entry[0] == SkipWorktree))
            .Select(entry => entry[TagLength..])];
    }

    /// <summary>The tag git ls-files -v puts on a skip-worktree file.</summary>
    private const char SkipWorktree = 'S';

    /// <summary>The letter and the space git ls-files -v puts before a path.</summary>
    private const int TagLength = 2;

    public async Task<bool> IsRepositoryAsync(string root, CancellationToken ct) =>
        (await runner.RunAsync("git", ["rev-parse", "--is-inside-work-tree"], root, ct)).Code == 0;

    public async Task<string> PrefixAsync(string root, CancellationToken ct)
    {
        var prefix = await runner.RunAsync("git", ["rev-parse", "--show-prefix"], root, ct);
        return prefix.Code == 0 ? prefix.Output.Trim() : "";
    }

    public async Task<(string Commit, string Previous)?> CommitUncommittedAsync(string root, string message, CancellationToken ct)
    {
        if (await IsCleanAsync(root, ct))
        {
            return null;
        }

        var previous = (await runner.RunCheckedAsync("git", ["rev-parse", "HEAD"], root, ct)).Trim();
        // The solution's own index: what it holds becomes the commit's tree, so after update-ref the tree is clean
        // and a merge can run on it. What git ignores is not added, as in every other step.
        await runner.RunCheckedAsync("git", ["add", "-A", "--", "."], root, ct);
        var tree = (await runner.RunCheckedAsync("git", ["write-tree"], root, ct)).Trim();
        var identity = await IdentityArgsAsync(runner, root, ct);
        var commit = (await runner.RunCheckedAsync("git", [.. identity, "commit-tree", tree, "-p", previous, "-m", message], root, ct)).Trim();
        await runner.RunCheckedAsync("git", ["update-ref", "HEAD", commit, previous], root, ct);
        return (commit, previous);
    }

    public async Task UncommitAsync(string root, string commit, CancellationToken ct) =>
        await runner.RunCheckedAsync("git", ["reset", "--mixed", commit], root, ct);

    public async Task CommitAsync(string root, string message, CancellationToken ct)
    {
        var previous = (await runner.RunCheckedAsync("git", ["rev-parse", "HEAD"], root, ct)).Trim();
        // The commit concludes the merge git staged with --no-commit: it keeps MERGE_HEAD as the second parent, the
        // way git commit does, so the branch's tip stays the known base of the next part.
        var mergeHead = await runner.RunAsync("git", ["rev-parse", "-q", "--verify", "MERGE_HEAD"], root, ct);
        await runner.RunCheckedAsync("git", ["add", "-A", "--", "."], root, ct);
        var tree = (await runner.RunCheckedAsync("git", ["write-tree"], root, ct)).Trim();
        var identity = await IdentityArgsAsync(runner, root, ct);
        string[] parents = mergeHead.Code == 0 ? ["-p", previous, "-p", mergeHead.Output.Trim()] : ["-p", previous];
        var commit = (await runner.RunCheckedAsync("git", [.. identity, "commit-tree", tree, .. parents, "-m", message], root, ct)).Trim();
        await runner.RunCheckedAsync("git", ["update-ref", "HEAD", commit, previous], root, ct);
        // A commit by plumbing does not conclude the merge: its state files stay and the next merge would be refused,
        // so they go the way git commit removes them.
        var gitDir = (await runner.RunCheckedAsync("git", ["rev-parse", "--absolute-git-dir"], root, ct)).Trim();
        foreach (var state in new[] { "MERGE_HEAD", "MERGE_MODE", "MERGE_MSG" })
        {
            var file = Path.Combine(gitDir, state);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    public async Task<bool> MergeInProgressAsync(string root, CancellationToken ct) =>
        (await runner.RunAsync("git", ["rev-parse", "-q", "--verify", "MERGE_HEAD"], root, ct)).Code == 0;
}
