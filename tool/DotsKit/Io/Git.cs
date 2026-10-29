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
}

/// <summary>
/// <c>git status --porcelain</c> and <c>git rev-parse --show-prefix</c>. Every git command here is listed in docs/getting-started/dotskit.md, "What dotskit
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

    public async Task<string> PrefixAsync(string root, CancellationToken ct)
    {
        var prefix = await runner.RunAsync("git", ["rev-parse", "--show-prefix"], root, ct);
        return prefix.Code == 0 ? prefix.Output.Trim() : "";
    }
}
