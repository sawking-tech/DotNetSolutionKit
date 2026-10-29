using DotsKit.Io;

namespace DotsKit.Commands;

/// <summary>
/// What new and upgrade need of git before they plan a write into a solution: a clean working tree, so what they write
/// can be seen and undone, and, for the merge by git, the solution at the root of its repository.
/// </summary>
internal static class GitPreconditions
{
    public static async Task CheckAsync(IGit git, string root, Options options, CancellationToken ct)
    {
        // First, as no commit changes it. The branch dotskit/template holds the template's output at the root of a
        // tree, and git merges it at the root of the repository: a solution in a folder of a larger repository would
        // get it beside itself.
        if (options.ByGit && await git.PrefixAsync(root, ct) is { Length: > 0 } prefix)
        {
            throw new InvalidOperationException(
                $"The solution is in {prefix.TrimEnd('/')} of its git repository, and the merge by git works at the root of one: run the command with --merge files.");
        }

        if (!options.AllowDirty && !await git.IsCleanAsync(root, ct))
        {
            throw new InvalidOperationException("The git working tree has uncommitted changes: commit or stash them, so what dotskit writes can be seen and undone"
                + (options.Force ? "; --force needs them committed." : " (--allow-dirty to go on)."));
        }

        if (options.AllowDirty && options.Force)
        {
            throw new ArgumentException("--force needs a clean tree: the team's lines it replaces stay in git history only when they are committed.");
        }

        if (options.AllowDirty && options.ByGit)
        {
            throw new ArgumentException("--allow-dirty needs --merge files: a git merge runs on a clean tree.");
        }
    }
}
