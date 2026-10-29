using DotsKit.Io;

namespace DotsKit.Merging;

/// <summary>
/// What the template changes between two of its outputs, a file each: what a merge by git brings, shown before it
/// runs. The solution's side is git's to merge, so the plan names the template's change only.
/// </summary>
internal static class TemplateDiff
{
    public static List<Change> Of(string baseDir, string nextDir) =>
        new SortedSet<string>(Folders.RelativeFiles(baseDir).Concat(Folders.RelativeFiles(nextDir)), StringComparer.Ordinal)
            .Select(path => Decide(path, FileText.Read(baseDir, path), FileText.Read(nextDir, path)))
            .OfType<Change>()
            .ToList();

    /// <summary>Pure: a file of the two outputs, or nothing when the template left it as it was.</summary>
    public static Change? Decide(string path, FileText? @base, FileText? next) =>
        (@base, next) switch
        {
            (null, null) => null,
            (null, { } added) => new(path, ChangeKind.Add, added, "new in the template"),
            ({ }, null) => new(path, ChangeKind.Delete, null, "removed from the template"),
            ({ } was, { } now) when now.SameTextAs(was) => null,
            (_, { } now) => new(path, ChangeKind.Update, now, "changed in the template"),
        };
}
