using DotsKit.Io;

namespace DotsKit.Ignoring;

/// <summary>
/// The candidates a set of gitignore patterns answers to, matched by git itself: an excludes file of the tool's own
/// holds the patterns, and <c>git check-ignore --no-index --stdin</c> names the candidates one of them matches, with the
/// rule that decided each. The match runs in an empty repository of the tool's own, so neither the solution's gitignore
/// nor its config reaches it, and a solution outside git reads its rules the same way. Nothing of the solution is
/// touched and no file of it is written. Every git command here is listed in docs/getting-started/tool.md, "What
/// dotskit runs in git": a command added or changed here goes there too.
/// </summary>
/// <param name="runner">Runs git.</param>
internal sealed class GitIgnorePatterns(IProcessRunner runner) : IIgnorePatterns
{
    public async Task<IReadOnlyList<IgnoreMatch>> MatchAsync(string root, IReadOnlyList<string> patterns, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        if (patterns.Count == 0 || candidates.Count == 0)
        {
            return [];
        }

        var work = Folders.NewTemp("ignore");
        var repository = Path.Combine(work, "repository");
        Directory.CreateDirectory(repository);
        await runner.RunCheckedAsync("git", ["init", "-q", "."], repository, ct);
        var excludes = Path.Combine(work, "excludes");
        await File.WriteAllTextAsync(excludes, string.Join('\n', patterns) + "\n", ct);
        // -v names the rule that decided each candidate: the line of the excludes file, its pattern, then the path. git
        // resolves the path of an excludes file against the repository's folder, so it goes whole.
        var listed = await runner.RunAsync("git",
            ["-c", $"core.excludesFile={excludes.Replace('\\', '/')}", "check-ignore", "--no-index", "-v", "-z", "--stdin"], repository, ct,
            input: string.Join('\0', candidates) + "\0");
        // check-ignore exits with 1 when none of the candidates matches, which is not a failure.
        return listed.Code switch
        {
            0 => Parse(listed.Output),
            1 => [],
            _ => throw new InvalidOperationException($"git could not read the rules of {IgnoreRules.FileName}: {listed.Error.Trim()}"),
        };
    }

    /// <summary>git's -z output with -v: source, line, pattern, path per match, each ended by NUL.</summary>
    private static List<IgnoreMatch> Parse(string output)
    {
        var fields = output.Split('\0', StringSplitOptions.None);
        List<IgnoreMatch> matches = [];
        // The last field is empty after the final NUL; a group is four fields.
        for (var i = 0; i + 3 < fields.Length; i += 4)
        {
            if (fields[i].Length == 0 || !int.TryParse(fields[i + 1], out var line))
            {
                break;
            }

            matches.Add(new IgnoreMatch(fields[i + 2], line, fields[i + 3]));
        }

        return matches;
    }
}

/// <summary>One candidate a rule of the file answers to: the rule's text, its line, and the path it decided.</summary>
/// <param name="Pattern">The rule as the file writes it, a line starting with <c>!</c> keeping it.</param>
/// <param name="Line">The line of the file the rule is on.</param>
/// <param name="Path">The candidate, relative to the solution's root with forward slashes.</param>
internal sealed record IgnoreMatch(string Pattern, int Line, string Path);
