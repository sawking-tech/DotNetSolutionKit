using System.Text.RegularExpressions;

namespace DotsKit.Solutions;

/// <summary>A type of the template that the next version keeps in another project or namespace.</summary>
internal sealed record TypeMove(TypeHome From, TypeHome To);

/// <summary>
/// The types the template moved between the version a solution has and the one it goes to. A type is followed
/// inside its own area - a service, or the code the services share - so a service's copy of a type never leads to
/// another service's: in its own project first, else the only type of the name in the area. A type with two homes
/// there is not followed.
/// </summary>
internal sealed record TypeMap(IReadOnlyList<TypeMove> Moves, IReadOnlySet<string> NextNamespaces)
{
    public static TypeMap Between(TypeIndex before, TypeIndex after) =>
        new([.. before.All.Select(t => (From: t, To: NextHomeOf(t, after))).Where(m => m.To is not null && m.To != m.From).Select(m => new TypeMove(m.From, m.To!))],
            after.All.Select(t => t.Namespace).ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// The moves a step of an upgrade a part at a time follows: those whose two projects are merged. A namespace a
    /// move out of which waits for its part still counts as kept, so the usings of it stay until that part.
    /// </summary>
    /// <param name="merged">Whether a project, relative to the root, is in a part merged so far or in this one.</param>
    public TypeMap Within(Func<string, bool> merged)
    {
        var waiting = Moves.Where(m => !merged(m.From.Project) || !merged(m.To.Project)).ToList();
        return waiting.Count == 0
            ? this
            : new([.. Moves.Except(waiting)], NextNamespaces.Union(waiting.Select(m => m.From.Namespace)).ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>The service folder a project is in, or null for the code the services share.</summary>
    public static string? AreaOf(string project) => Layout.ServiceFolderOf(project);

    /// <summary>The moves out of a namespace the next version no longer has.</summary>
    public IEnumerable<TypeMove> OutOf(string ns) =>
        NextNamespaces.Contains(ns) ? [] : Moves.Where(m => m.From.Namespace == ns);

    private static TypeHome? NextHomeOf(TypeHome type, TypeIndex after)
    {
        var area = after.Find(type.Name).Where(h => AreaOf(h.Project) == AreaOf(type.Project)).ToList();
        var own = area.Where(h => h.Project == type.Project).ToList();
        return own is [var same] ? same : area is [var only] ? only : null;
    }
}

/// <summary>A C# file of the solution: its path, its project, the namespaces it has a using of and those it declares.</summary>
internal sealed record SourceFile(string File, string Project, IReadOnlyList<string> Usings, IReadOnlyList<string> Declares);

/// <summary>A change to the team's code that a type of the template moving to another project or namespace asks for.</summary>
internal abstract record Repair;

/// <summary>A project references the project a type it uses lives in now; both full paths.</summary>
internal sealed record AddReference(string Project, string Referenced) : Repair;

/// <summary>A file takes the namespace a type it uses lives in now.</summary>
internal sealed record AddUsing(string File, string Namespace) : Repair;

/// <summary>A file's using of a namespace the template gave up becomes the usings of the namespaces its types went to.</summary>
internal sealed record ReplaceUsing(string File, string Namespace, string With) : Repair;

/// <summary>
/// Decides the repairs from the errors of a build: a using of a namespace the template gave up is replaced by where
/// its types went; a type not found that the template moved gets its namespace used and its project referenced.
/// Only the moves of the file's own area and of the shared code count, so a service never gets a using or a
/// reference of another service; a type the template did not move is left to a person.
/// </summary>
internal static partial class ReferenceRepairs
{
    private const string TypeNotFound = "CS0246";
    private const string NotInNamespace = "CS0234";

    [GeneratedRegex(@"^The type or namespace name '(?<name>[\w.]+)(<[^']*>)?'( does not exist in the namespace '(?<ns>[\w.]+)')?")]
    private static partial Regex Missing();

    /// <param name="errors">The errors of the build that failed.</param>
    /// <param name="map">The types the template moved between the solution's version and the next.</param>
    /// <param name="root">The solution's root, the paths of the projects are relative to.</param>
    /// <param name="usingsOf">The namespaces a file has a using of: the compiler names only the first segment it misses.</param>
    public static IReadOnlyList<Repair> Decide(IEnumerable<BuildError> errors, TypeMap map, string root, Func<string, IReadOnlyList<string>> usingsOf) =>
        errors.SelectMany(e => Of(e, map, root, usingsOf)).Distinct().ToList();

    /// <summary>
    /// The usings that name a namespace the template gave up, decided from the text with no build: what the build
    /// would report first as CS0234. A namespace the solution's own code still declares holds the team's types, and
    /// its usings are left to the build.
    /// </summary>
    public static IReadOnlyList<Repair> FromText(IEnumerable<SourceFile> files, TypeMap map, string root)
    {
        var all = files.ToList();
        var declared = all.SelectMany(f => f.Declares).ToHashSet(StringComparer.Ordinal);
        return all.SelectMany(f => f.Usings
                .Where(u => !declared.Contains(u))
                .Select(u => (Using: u, Moves: map.OutOf(u).Where(m => Reaches(TypeMap.AreaOf(Relative(root, f.Project)), m.To)).ToList()))
                .Where(g => g.Moves.Count > 0)
                .SelectMany(g => ForGivenUp(f.File, f.Project, g.Using, g.Moves, root)))
            .Distinct()
            .ToList();
    }

    private static IEnumerable<Repair> Of(BuildError error, TypeMap map, string root, Func<string, IReadOnlyList<string>> usingsOf)
    {
        if (error.Code is not (TypeNotFound or NotInNamespace) || Missing().Match(error.Message) is not { Success: true } m)
        {
            return [];
        }

        var area = TypeMap.AreaOf(Relative(root, error.Project));
        var name = m.Groups["name"].Value;
        var missing = m.Groups["ns"].Success ? $"{m.Groups["ns"].Value}.{name}" : name;
        var given = usingsOf(error.File)
            .Where(u => u == missing || u.StartsWith(missing + ".", StringComparison.Ordinal))
            .Select(u => (Using: u, Moves: map.OutOf(u).Where(mv => Reaches(area, mv.To)).ToList()))
            .Where(g => g.Moves.Count > 0)
            .ToList();
        if (given.Count > 0)
        {
            return given.SelectMany(g => ForGivenUp(error.File, error.Project, g.Using, g.Moves, root));
        }

        return error.Code == TypeNotFound ? ForType(error, name, area, usingsOf(error.File), map, root) : [];
    }

    private static IEnumerable<Repair> ForGivenUp(string file, string project, string old, IReadOnlyList<TypeMove> moves, string root) =>
        [.. moves.Select(m => m.To.Namespace).Distinct().Select(ns => new ReplaceUsing(file, old, ns)),
            .. References(project, moves.Select(m => m.To.Project).Distinct(), root)];

    /// <summary>
    /// The type's move in the file's own area first, else in the shared code; two homes are left as they are. Only a
    /// move out of a namespace the file uses counts: the file found the type there before, and a type of the team's
    /// that has a template's name is not the template's.
    /// </summary>
    private static IEnumerable<Repair> ForType(BuildError error, string name, string? area, IReadOnlyList<string> usings, TypeMap map, string root)
    {
        var moves = map.Moves.Where(m => m.To.Name == name && Reaches(area, m.To) && usings.Contains(m.From.Namespace)).Select(m => m.To).Distinct().ToList();
        var own = moves.Where(h => TypeMap.AreaOf(h.Project) == area).ToList();
        return (own.Count > 0 ? own : moves) is [var home]
            ? [new AddUsing(error.File, home.Namespace), .. References(error.Project, [home.Project], root)]
            : [];
    }

    /// <summary>A file of a service reaches its own service and the shared code; a file of the shared code, only that.</summary>
    private static bool Reaches(string? area, TypeHome home) => TypeMap.AreaOf(home.Project) is not { } other || other == area;

    private static IEnumerable<Repair> References(string project, IEnumerable<string> projects, string root) =>
        projects.Select(p => Path.GetFullPath(Path.Combine(root, p)))
            .Where(p => !SamePath(p, project))
            .Select(p => new AddReference(Path.GetFullPath(project), p));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
