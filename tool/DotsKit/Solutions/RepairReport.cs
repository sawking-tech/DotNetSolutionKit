namespace DotsKit.Solutions;

/// <summary>
/// What the repair of usings and references changed, a line per project of the solution: the usings it replaced or
/// added and the references it added, so the team sees where the template's moves reached its code.
/// </summary>
internal static class RepairReport
{
    /// <summary>The file a repair changes: the project for a reference, the source file for a using.</summary>
    public static string FileOf(Repair repair) => repair switch
    {
        AddReference r => r.Project,
        RemoveReference r => r.Project,
        CentralizeVersion c => c.Project,
        AddUsing u => u.File,
        ReplaceUsing u => u.File,
        ReplaceQualified q => q.File,
        _ => throw new ArgumentOutOfRangeException(nameof(repair), repair, "A repair of an unknown kind."),
    };

    /// <summary>A line per project, in order of name: "  Project: using A -> B (3 files); reference to C".</summary>
    public static IReadOnlyList<string> ByProject(IEnumerable<Repair> repairs) =>
        [.. repairs.GroupBy(r => ProjectOf(FileOf(r)), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"  {g.Key}: {string.Join("; ", Changes(g))}")];

    private static IEnumerable<string> Changes(IEnumerable<Repair> repairs) =>
        repairs.Select(Describe)
            .GroupBy(d => d, StringComparer.Ordinal)
            .Select(g => g.Count() > 1 ? $"{g.Key} ({g.Count()} files)" : g.Key);

    private static string Describe(Repair repair) => repair switch
    {
        ReplaceUsing u => $"using {u.Namespace} -> {u.With}",
        AddUsing u => $"using {u.Namespace} added",
        ReplaceQualified q => $"{q.Namespace}.{q.Type} -> {q.With}.{q.Type}",
        AddReference r => $"reference to {Path.GetFileNameWithoutExtension(r.Referenced)}",
        CentralizeVersion c => c.Kept is { } kept && kept != c.Version
            ? $"{c.Package} {c.Version} kept as VersionOverride, Directory.Packages.props has {kept}"
            : $"{c.Package} {c.Version} -> Directory.Packages.props",
        RemoveReference r => $"reference to {Path.GetFileNameWithoutExtension(r.Referenced)} removed, as the template has it",
        _ => repair.ToString(),
    };

    /// <summary>The name of the project a file is in: the file itself for a project, else the nearest project above it.</summary>
    private static string ProjectOf(string file)
    {
        if (file.EndsWith(Layout.ProjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(file);
        }

        for (var dir = Path.GetDirectoryName(file); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (Directory.Exists(dir) && Directory.GetFiles(dir, "*" + Layout.ProjectExtension) is [var project])
            {
                return Path.GetFileNameWithoutExtension(project);
            }
        }

        return Path.GetFileName(Path.GetDirectoryName(file)) ?? file;
    }
}
