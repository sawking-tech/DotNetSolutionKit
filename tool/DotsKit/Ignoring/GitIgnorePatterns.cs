using DotsKit.Io;

namespace DotsKit.Ignoring;

/// <summary>
/// The candidates a set of <c>.dotskitignore</c> files answer to, matched by git itself: the files are written into an
/// empty repository of the tool's own as <c>.gitignore</c> files, at the folders they sit in, and
/// <c>git check-ignore --no-index --stdin</c> names the candidates one of them matches, with the file and the line that
/// decided each. git then owns the whole semantics the ADR borrows: a rule acts on the subtree of its folder, a deeper
/// file overrides a higher one, a later line overrides an earlier one, and <c>!</c> cancels. The repository is empty and
/// of the tool's own, so neither the solution's <c>.gitignore</c> nor its config reaches the match, and a solution
/// outside git reads its rules the same way. Nothing of the solution is touched and no file of it is written. Every git
/// command here is listed in docs/getting-started/tool.md, "What dotskit runs in git": a command added or changed here
/// goes there too.
/// </summary>
/// <param name="runner">Runs git.</param>
internal sealed class GitIgnorePatterns(IProcessRunner runner) : IIgnorePatterns
{
    public async Task<IReadOnlyList<IgnoreMatch>> MatchAsync(string root, IReadOnlyList<DotskitIgnore> files, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        if (files.Count == 0 || candidates.Count == 0)
        {
            return [];
        }

        var repository = Path.Combine(Folders.NewTemp("ignore"), "repository");
        Directory.CreateDirectory(repository);
        await runner.RunCheckedAsync("git", ["init", "-q", "."], repository, ct);
        foreach (var file in files)
        {
            await WriteAsync(repository, file, ct);
        }

        var listed = await runner.RunAsync("git", ["check-ignore", "--no-index", "-v", "-z", "--stdin"], repository, ct,
            input: string.Join('\0', candidates) + "\0");
        // check-ignore exits with 1 when none of the candidates matches, which is not a failure.
        return listed.Code switch
        {
            0 => Parse(repository, listed.Output),
            1 => [],
            _ => throw new InvalidOperationException($"git could not read the rules of {DotskitIgnores.FileName}: {listed.Error.Trim()}"),
        };
    }

    /// <summary>
    /// A file of the solution in the folder it sits in, named <c>.gitignore</c> for git to read it. Every line is kept
    /// where it stands, a comment and a blank too - the only change a line takes is the pin, which git knows nothing of
    /// -, so the line git reports is the line of the file the team edits.
    /// </summary>
    private static async Task WriteAsync(string repository, DotskitIgnore file, CancellationToken ct)
    {
        var lines = file.Text.Split('\n');
        foreach (var rule in file.Rules.Where(r => r.Pin is not null))
        {
            lines[rule.Line - 1] = rule.Pattern;
        }

        var folder = file.Source.Length == 0 ? repository : Path.Combine(repository, file.Source);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, ".gitignore"), string.Join('\n', lines), ct);
    }

    /// <summary>
    /// git's -z output with -v: source, line, pattern, path per match, each ended by NUL. The source is the
    /// <c>.gitignore</c> the tool wrote, which is the folder of the <c>.dotskitignore</c> it came from.
    /// </summary>
    private static List<IgnoreMatch> Parse(string repository, string output)
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

            matches.Add(new IgnoreMatch(fields[i + 2], SourceOf(repository, fields[i]), line, fields[i + 3]));
        }

        return matches;
    }

    /// <summary>The folder of the file git read, relative to the solution's root; empty at the root.</summary>
    private static string SourceOf(string repository, string source)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(repository, source)))!;
        var relative = Path.GetRelativePath(repository, folder).Replace('\\', '/');
        return relative == "." ? "" : relative;
    }
}

/// <summary>One candidate a rule of a file answers to: the rule's text, the file it sits in, its line, and the path it decided.</summary>
/// <param name="Pattern">The rule as git read it, a line starting with <c>!</c> keeping it and the pin not a part of it.</param>
/// <param name="Source">The folder of the <c>.dotskitignore</c> the rule sits in, relative to the solution's root; empty at the root.</param>
/// <param name="Line">The line of that file the rule is on.</param>
/// <param name="Path">The candidate, relative to the solution's root with forward slashes.</param>
internal sealed record IgnoreMatch(string Pattern, string Source, int Line, string Path);
