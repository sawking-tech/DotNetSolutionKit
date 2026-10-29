using DotsKit.Io;

namespace DotsKit.Merging;

/// <param name="Text">The merged text, with LF line ends; empty when the texts could not be merged.</param>
/// <param name="Conflicts">The number of places both sides changed; they are marked in the text.</param>
/// <param name="Failure">Why the texts could not be merged at all; null when they were.</param>
internal sealed record TextMergeResult(string Text, int Conflicts, string? Failure = null);

/// <summary>Merges two texts changed from one base; texts come and go with LF line endings.</summary>
internal interface ITextMerger
{
    Task<TextMergeResult> MergeAsync(string solution, string @base, string template, bool preferTemplate, CancellationToken ct);
}

/// <summary><c>git merge-file</c>: conflicts are marked &lt;&lt;&lt;&lt;&lt;&lt;&lt; solution / &gt;&gt;&gt;&gt;&gt;&gt;&gt; template.</summary>
internal sealed class GitMergeFile(IProcessRunner runner) : ITextMerger
{
    private const int MaxConflicts = 127;

    public async Task<TextMergeResult> MergeAsync(string solution, string @base, string template, bool preferTemplate, CancellationToken ct)
    {
        var work = Folders.NewTemp("merge");
        var files = new[] { ("solution", solution), ("base", @base), ("template", template) }
            .Select(f => (Label: f.Item1, Path: Path.Combine(work, f.Item1), Text: f.Item2)).ToList();
        foreach (var file in files)
        {
            await File.WriteAllTextAsync(file.Path, file.Text, ct);
        }

        List<string> args = ["merge-file", "-p", .. files.SelectMany(f => new[] { "-L", f.Label })];
        if (preferTemplate)
        {
            args.Add("--theirs");
        }

        args.AddRange(files.Select(f => f.Path));

        var result = await runner.RunAsync("git", args, work, ct);
        // git merge-file exits with the number of conflicts, at most 127; above that it could not merge at all
        // (255), and its empty output must not be taken for the file.
        if (result.Code > MaxConflicts)
        {
            return new(string.Empty, 0, result.Error.Trim());
        }

        return new(result.Output, result.Code);
    }
}
