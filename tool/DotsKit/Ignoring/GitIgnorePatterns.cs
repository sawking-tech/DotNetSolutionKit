using DotsKit.Io;

namespace DotsKit.Ignoring;

/// <summary>
/// The candidates a set of gitignore patterns answers to, matched by git itself: an excludes file of the tool's own
/// holds the patterns, and <c>git check-ignore --no-index --stdin</c> names the candidates one of them matches. The
/// match runs in an empty repository of the tool's own, so neither the solution's gitignore nor its config reaches it,
/// and a solution outside git reads its rules the same way as one inside it. Nothing of the solution is touched and no
/// file of it is written. Every git command here is listed in docs/getting-started/tool.md, "What dotskit runs in
/// git": a command added or changed here goes there too.
/// </summary>
/// <param name="runner">Runs git.</param>
internal sealed class GitIgnorePatterns(IProcessRunner runner) : IIgnorePatterns
{
    public async Task<IReadOnlySet<string>> MatchAsync(string root, IReadOnlyList<string> patterns, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        if (patterns.Count == 0 || candidates.Count == 0)
        {
            return new HashSet<string>();
        }

        var work = Folders.NewTemp("ignore");
        var repository = Path.Combine(work, "repository");
        Directory.CreateDirectory(repository);
        await runner.RunCheckedAsync("git", ["init", "-q", "."], repository, ct);
        var excludes = Path.Combine(work, "excludes");
        await File.WriteAllTextAsync(excludes, string.Join('\n', patterns) + "\n", ct);
        // git resolves the path of an excludes file against the repository's folder, so it goes whole.
        var listed = await runner.RunAsync("git",
            ["-c", $"core.excludesFile={excludes.Replace('\\', '/')}", "check-ignore", "--no-index", "-z", "--stdin"], repository, ct,
            input: string.Join('\0', candidates) + "\0");
        // check-ignore exits with 1 when none of the candidates matches, which is not a failure.
        return listed.Code is 0 or 1
            ? new HashSet<string>(listed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal)
            : throw new InvalidOperationException($"git could not read the rules of {IgnoreRules.FileName}: {listed.Error.Trim()}");
    }
}
