using DotsKit.Io;

namespace DotsKit.Merging;

/// <summary>
/// The files a merge reads a style from: every C# file of the template's next output that the team has in its
/// solution and did not edit alone. Two cases bring one in: a file both sides changed - the template against the
/// output the solution came from, the team in its solution - and a file the template added, which has no base to
/// compare against but arrives whole into the team's solution. A file the team alone touched is not here: laying it
/// out would rewrite code neither side asked to change, and the merge has no template side for it anyway.
/// </summary>
internal static class BothChanged
{
    /// <summary>The paths of the C# files to lay out, in the order of the paths.</summary>
    /// <param name="root">The solution's root, where the team's files are.</param>
    /// <param name="baseDir">The template's output the solution came from.</param>
    /// <param name="nextDir">The template's output the solution goes to.</param>
    public static IReadOnlyList<string> CSharp(string root, string baseDir, string nextDir) =>
        [.. Folders.RelativeFiles(nextDir)
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => ChangedAgainst(baseDir, nextDir, p) && (FileText.Read(baseDir, p) is null || ChangedAgainst(baseDir, root, p)))
            .Order(StringComparer.Ordinal)];

    /// <summary>Whether the file at <paramref name="path"/> in <paramref name="side"/> differs from the base's, its absence included.</summary>
    private static bool ChangedAgainst(string baseDir, string side, string path)
    {
        var before = FileText.Read(baseDir, path);
        var now = FileText.Read(side, path);
        return (before, now) switch
        {
            (null, null) => false,
            (null, { }) or ({ }, null) => true,
            ({ } was, { } isNow) => !isNow.SameTextAs(was),
        };
    }
}
