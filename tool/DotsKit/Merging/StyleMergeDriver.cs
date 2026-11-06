namespace DotsKit.Merging;

/// <summary>
/// The merge driver git runs for a file both the solution and the template changed, in the merge of
/// <see cref="GitTemplateMerge"/>: <c>dotskit merge-driver %O %A %B %P</c>. It brings the three sides to
/// <see cref="TemplateStyle"/> and merges them by the same rule, so a change of the template's style alone
/// conflicts with nothing the team wrote, in either mode. The result goes into %A, the solution's side, as git asks;
/// a conflict is marked in it and the exit code says there is one, and git lists the file as unmerged for the IDE.
/// A driver, and not <c>git merge -X renormalize</c> with a clean filter, which was tried on 29.10.2026 and merges
/// the three sides as well: with core.autocrlf=true the filter makes a clean working tree look changed to git, and
/// the merge refuses to start. A driver sees the three sides as files of their own and never the working tree.
/// </summary>
internal sealed class StyleMergeDriver(ITextMerger merger)
{
    public const string Command = "merge-driver";

    /// <summary>The driver's last argument when the command takes the template's side in a conflict (--force).</summary>
    public const string TheirsFlag = "--theirs";

    /// <summary>The driver's argument when a conflict is marked with its base, as zdiff3 writes it (--conflict-style zdiff3).</summary>
    public const string Zdiff3Flag = "--zdiff3";

    private const int Merged = 0;
    private const int Conflicted = 1;

    /// <param name="args">The base, the solution's and the template's file, the path in the solution, maybe <see cref="TheirsFlag"/> and <see cref="Zdiff3Flag"/>.</param>
    /// <param name="ct">Cancels the merge.</param>
    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        if (args.Count is < 4 or > 6)
        {
            throw new ArgumentException($"dotskit {Command} is run by git as {Command} %O %A %B %P [{TheirsFlag}] [{Zdiff3Flag}]; it is not a command to run by hand.");
        }

        var path = args[3];
        var flags = args.Skip(4).ToHashSet(StringComparer.Ordinal);
        var unknown = flags.Except([TheirsFlag, Zdiff3Flag]).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"dotskit {Command} does not take {string.Join(' ', unknown)}.");
        }

        var (@base, solution, template) = (Read(args[0]), Read(args[1]), Read(args[2]));
        // A binary side is not text to merge: git keeps the solution's side and lists the file as unmerged.
        if (@base.IsBinary || solution.IsBinary || template.IsBinary)
        {
            return Conflicted;
        }

        (@base, solution, template) = (TemplateStyle.Of(path, @base), TemplateStyle.Of(path, solution), TemplateStyle.Of(path, template));
        var result = await merger.MergeAsync(LineEndings.ToLf(solution.Text), LineEndings.ToLf(@base.Text), LineEndings.ToLf(template.Text),
            flags.Contains(TheirsFlag), flags.Contains(Zdiff3Flag), ct);
        if (result.Failure is not null)
        {
            throw new InvalidOperationException(
                $"git could not merge {path}: {result.Failure}. The file holds the solution's side: take the template's with git checkout --theirs -- {path}, or merge it by hand.");
        }

        // The merged text in the line ends of the solution's side, as the tool writes it.
        await File.WriteAllBytesAsync(args[1], (solution with { Text = LineEndings.Apply(result.Text, LineEndings.Of(solution.Text)) }).ToBytes(), ct);
        // --force: the merge that started this driver names each file whose conflict took the template's lines.
        if (flags.Contains(TheirsFlag) && result.Taken > 0
            && Environment.GetEnvironmentVariable(GitTemplateMerge.TakenReportVariable) is { Length: > 0 } report)
        {
            await File.AppendAllTextAsync(report, path + "\n", ct);
        }
        return result.Conflicts == 0 ? Merged : Conflicted;
    }

    /// <summary>A side git gave as a file; an empty one when the side has no such file, as for a file both added.</summary>
    private static FileText Read(string file) => FileText.Read(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetFileName(file)) ?? new(string.Empty, false);
}
