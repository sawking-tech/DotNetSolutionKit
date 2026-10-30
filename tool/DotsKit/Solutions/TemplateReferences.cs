using System.Text.RegularExpressions;

namespace DotsKit.Solutions;

/// <summary>
/// The references between the template's projects change as the template changes them, whatever else the merge
/// left: a reference the template's base output has and its next one has not goes from the solution's project, one
/// the next output adds comes. Only the template's own change is carried, as a three-way merge carries it: a
/// reference the team added or removed stays as the team has it, and a repair's reference stays too. What the
/// template's earlier version got wrong - Common referencing Contracts in 1.0 - goes, since the base has it. A
/// project left in conflict waits for its resolution.
/// </summary>
internal static partial class TemplateReferences
{
    [GeneratedRegex(@"<ProjectReference\b[^>]*?\sInclude=""(?<path>[^""]+)""")]
    private static partial Regex Reference();

    /// <param name="root">The solution's root.</param>
    /// <param name="baseTree">The template's output the solution came from.</param>
    /// <param name="nextTree">The template's next output the solution was merged with.</param>
    /// <param name="conflicts">The files left in conflict, relative to the root.</param>
    public static IReadOnlyList<Repair> Decide(string root, string baseTree, string nextTree, IReadOnlyCollection<string> conflicts)
    {
        var repairs = new List<Repair>();
        foreach (var project in ProjectsOf(nextTree).Where(p => File.Exists(Path.Combine(root, p)) && !conflicts.Contains(p)))
        {
            var before = File.Exists(Path.Combine(baseTree, project)) ? ReferencesOf(baseTree, project).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
            var after = ReferencesOf(nextTree, project).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var full = Path.GetFullPath(Path.Combine(root, project));
            repairs.AddRange(before.Except(after, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
                .Select(r => new RemoveReference(full, Path.GetFullPath(Path.Combine(root, r)))));
            repairs.AddRange(after.Except(before, StringComparer.OrdinalIgnoreCase).Where(r => File.Exists(Path.Combine(root, r))).Order(StringComparer.Ordinal)
                .Select(r => new AddReference(full, Path.GetFullPath(Path.Combine(root, r)))));
        }

        return repairs;
    }

    private static IEnumerable<string> ProjectsOf(string tree) =>
        Io.Folders.RelativeFiles(tree).Where(f => f.EndsWith(Layout.ProjectExtension, StringComparison.OrdinalIgnoreCase));

    /// <summary>The projects a project references, relative to the tree's root, with forward slashes.</summary>
    private static IEnumerable<string> ReferencesOf(string tree, string project)
    {
        var path = Path.Combine(tree, project);
        return Reference().Matches(File.ReadAllText(path))
            .Select(m => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, m.Groups["path"].Value.Replace('\\', '/'))))
            .Select(full => Path.GetRelativePath(tree, full).Replace('\\', '/'));
    }
}
