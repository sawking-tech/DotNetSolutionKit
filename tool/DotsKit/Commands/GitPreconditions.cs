using DotsKit.Io;

namespace DotsKit.Commands;

/// <summary>
/// What new and upgrade need of git before they plan a write into a solution: a clean working tree, so what they write
/// can be seen and undone (--allow-dirty: the changes are committed beside the run, and a failed run puts them back
/// uncommitted), no file git hides the changes of, and the solution at the root of its repository, where the merge by
/// git runs.
/// </summary>
internal static class GitPreconditions
{
    public static async Task CheckAsync(IGit git, string root, Options options, CancellationToken ct)
    {
        // The branch dotskit/template holds the template's output at the root of a tree, and git merges it at the root
        // of the repository: a solution in a folder of a larger repository would get it beside itself.
        if (await git.PrefixAsync(root, ct) is { Length: > 0 } prefix)
        {
            throw new InvalidOperationException(
                $"The solution is in {prefix.TrimEnd('/')} of its git repository, and the merge by git works at the root of one: move the solution to its own repository, then run the command again.");
        }

        if (await git.HiddenChangesAsync(root, ct) is [_, ..] hidden)
        {
            throw new InvalidOperationException($"git hides the local changes of {string.Join(", ", hidden)} (assume-unchanged or skip-worktree), and dotskit could not keep them: "
                + "git update-index --no-assume-unchanged --no-skip-worktree on them, commit or stash what they hold, then run again.");
        }

        if (!await git.IsRepositoryAsync(root, ct))
        {
            throw new InvalidOperationException("The solution is not in a git repository, and dotskit writes only what git can show and undo: git init, add and commit it, then run again.");
        }

        if (!options.AllowDirty && !await git.IsCleanAsync(root, ct))
        {
            throw new InvalidOperationException("The git working tree has uncommitted changes: commit or stash them, so what dotskit writes can be seen and undone"
                + (options.Force ? "; --force needs them committed." : " (--allow-dirty to go on: the changes are committed beside the run and taken into the merge)."));
        }

        if (options.AllowDirty && options.Force)
        {
            throw new ArgumentException("--force needs a clean tree: the team's lines it replaces stay in git history only when they are committed.");
        }
    }
}
