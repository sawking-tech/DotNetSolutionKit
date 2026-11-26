using DotsKit.Io;

namespace DotsKit.Merging;

/// <summary>
/// The files both sides of a merge changed: those the template changed against the output the solution came from, and
/// the team changed in its solution. The rest is nobody's change - a file the team alone touched keeps its bytes, a
/// file the template alone changed keeps the template's - and a style applied to them would rewrite code neither side
/// asked to change.
/// </summary>
internal static class BothChanged
{
    /// <summary>The paths of the C# files the two sides changed, in the order of the paths.</summary>
    /// <param name="root">The solution's root, where the team's files are.</param>
    /// <param name="baseDir">The template's output the solution came from.</param>
    /// <param name="nextDir">The template's output the solution goes to.</param>
    public static IReadOnlyList<string> CSharp(string root, string baseDir, string nextDir) =>
        [.. Folders.RelativeFiles(baseDir)
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => ChangedAgainst(baseDir, root, p) && ChangedAgainst(baseDir, nextDir, p))
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
