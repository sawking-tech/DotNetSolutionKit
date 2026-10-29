using System.Text.RegularExpressions;

namespace DotsKit.Solutions;

/// <summary>Where a public type of the template lives: its project, relative to the solution's root, and its namespace.</summary>
internal sealed record TypeHome(string Name, string Namespace, string Project);

/// <summary>
/// Where each public type of a generated tree lives. Taken from the tree the template generates for a version, so
/// it serves an upgrade to any version and is neither shipped nor written by hand (#117). Read by patterns, not by
/// a compiler: the template declares a type on a line of its own under a namespace of the file.
/// </summary>
internal sealed partial class TypeIndex
{
    private const string SourcePattern = "*.cs";

    private readonly Dictionary<string, List<TypeHome>> byName;

    private TypeIndex(IEnumerable<TypeHome> types) =>
        byName = types.Distinct()
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    [GeneratedRegex(@"^\s*namespace\s+(?<name>[\w.]+)\s*[;{]?\s*$", RegexOptions.Multiline)]
    private static partial Regex Namespace();

    [GeneratedRegex(@"^\s*public\s+(?:(?:static|sealed|abstract|partial|readonly|ref|unsafe|new)\s+)*(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+(?<name>\w+)", RegexOptions.Multiline)]
    private static partial Regex PublicType();

    /// <summary>The types of every project under <paramref name="root"/>, build output and git left out.</summary>
    public static TypeIndex Read(string root)
    {
        var projects = Directory.GetFiles(root, "*" + Layout.ProjectExtension, SearchOption.AllDirectories)
            .Where(p => !IsIgnored(root, p))
            .ToList();
        var folders = projects.Select(Path.GetDirectoryName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Of(projects.SelectMany(project => SourcesOf(Path.GetDirectoryName(project)!, folders)
            .Select(file => (Relative(root, project), File.ReadAllText(file)))));
    }

    /// <summary>The index of sources given as the project each belongs to and its text.</summary>
    public static TypeIndex Of(IEnumerable<(string Project, string Text)> sources) =>
        new(sources.SelectMany(s => TypesOf(s.Project, s.Text)));

    /// <summary>Every home of a type of this name: none when the template has no such type, more when the name is taken twice.</summary>
    public IReadOnlyList<TypeHome> Find(string name) => byName.TryGetValue(name, out var homes) ? homes : [];

    public IEnumerable<TypeHome> All => byName.Values.SelectMany(h => h);

    private static IEnumerable<TypeHome> TypesOf(string project, string text)
    {
        if (Namespace().Match(text) is not { Success: true } ns)
            return [];
        return PublicType().Matches(text).Select(m => new TypeHome(m.Groups["name"].Value, ns.Groups["name"].Value, project));
    }

    /// <summary>The sources of a project: its folder and below, without a folder of another project inside it.</summary>
    private static IEnumerable<string> SourcesOf(string folder, HashSet<string> projectFolders) =>
        Directory.GetFiles(folder, SourcePattern, SearchOption.AllDirectories)
            .Where(file => !IsIgnored(folder, file) && string.Equals(OwnerOf(file, projectFolders), folder, StringComparison.OrdinalIgnoreCase));

    private static string? OwnerOf(string file, HashSet<string> projectFolders)
    {
        for (var dir = Path.GetDirectoryName(file); dir is not null; dir = Path.GetDirectoryName(dir))
            if (projectFolders.Contains(dir))
                return dir;
        return null;
    }

    private static bool IsIgnored(string root, string path) =>
        Relative(root, path).Split('/').Any(segment => Layout.Ignored.Contains(segment));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
