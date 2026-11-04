using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.Reading;

/// <summary>
/// How well a manifest reproduces a solution: of the files the template gives for it, which the solution has
/// as they are. A file that differs is a change of the team or a wrong reading, and a person tells which before
/// anything relies on the manifest. Files the team added are not the template's and are not counted; All.sln is
/// compared by its projects, since its text differs by the GUIDs dotnet sln gives; the manifest, the build output
/// and the editors' folders are not the template's files either, as in the merges.
/// </summary>
internal sealed record Reproduction(int Total, IReadOnlyList<string> Differ)
{
    /// <summary>How many of the differing files a report lists; the rest it counts.</summary>
    public const int Listed = 30;

    public int Reproduced => Total - Differ.Count;

    public bool IsWhole => Differ.Count == 0;

    /// <param name="solution">The solution's root.</param>
    /// <param name="staged">What the template gives for the manifest, generated apart.</param>
    /// <param name="solutionProjects">The projects of the solution's All.sln.</param>
    /// <param name="stagedProjects">The projects of the generated All.sln.</param>
    public static Reproduction Of(string solution, string staged, IReadOnlySet<string> solutionProjects, IReadOnlySet<string> stagedProjects)
    {
        var files = Folders.RelativeFiles(staged).Where(f => !NotCompared(f)).Order(StringComparer.Ordinal).ToList();
        var differ = files.Where(f => FileText.Read(solution, f) is not { } own || !own.SameTextAs(FileText.Read(staged, f)!)).ToList();
        var missingProjects = stagedProjects.Where(p => !solutionProjects.Contains(p)).Order(StringComparer.Ordinal).Select(p => $"All.sln: {p}");
        return new(files.Count + stagedProjects.Count, [.. differ, .. missingProjects]);
    }

    /// <summary>All.sln, and what is not the template's files: the manifest, build output, the editors' folders.</summary>
    private static bool NotCompared(string path) =>
        (path.StartsWith(Layout.ServicesFolder + "/", StringComparison.Ordinal) && path.EndsWith(Layout.SolutionSuffix, StringComparison.Ordinal))
        || path.Split('/').Any(Layout.Ignored.Contains);

    /// <summary>The report's lines: the count, then the files that differ, the first <see cref="Listed"/> of them.</summary>
    public IEnumerable<string> Lines()
    {
        yield return $"{Reproduced} of {Total} files of the template reproduced" + (IsWhole ? "." : "; these differ:");
        foreach (var path in Differ.Take(Listed))
        {
            yield return "  " + path;
        }

        if (Differ.Count > Listed)
        {
            yield return $"  and {Differ.Count - Listed} more";
        }
    }
}
