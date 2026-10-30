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
}

/// <summary>
/// <c>git status --porcelain</c>, <c>git rev-parse --show-prefix</c> and <c>git ls-files -v</c>. Every git command here is listed in docs/getting-started/dotskit.md, "What dotskit
/// runs in git": a command added or changed here goes there too.
/// </summary>
internal sealed class Git(IProcessRunner runner) : IGit
{
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
        // git ls-files -v tags a file assume-unchanged with a lower case letter: "h path" instead of "H path".
        return files.Code != 0 ? [] : [.. files.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => entry.Length > TagLength && char.IsLower(entry[0]) && entry[1] == ' ').Select(entry => entry[TagLength..])];
    }

    /// <summary>The letter and the space git ls-files -v puts before a path.</summary>
    private const int TagLength = 2;

    public async Task<bool> IsRepositoryAsync(string root, CancellationToken ct) =>
        (await runner.RunAsync("git", ["rev-parse", "--is-inside-work-tree"], root, ct)).Code == 0;

    public async Task<string> PrefixAsync(string root, CancellationToken ct)
    {
        var prefix = await runner.RunAsync("git", ["rev-parse", "--show-prefix"], root, ct);
        return prefix.Code == 0 ? prefix.Output.Trim() : "";
    }
}
