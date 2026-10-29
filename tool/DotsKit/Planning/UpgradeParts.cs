namespace DotsKit.Planning;

/// <summary>
/// An upgrade a part at a time, as dotskit upgrade goes unless --all-at-once: the shared parts first - the root and deploy, then
/// src/common and its projects, then src/capabilities - then a service folder at a time, so a team resolves the
/// conflicts of one part before the next. A part is a set of paths; every step is the same merge, of the template's
/// next output for the parts merged so far and this one over its output for the parts merged before.
/// </summary>
internal static class UpgradeParts
{
    /// <summary>Everything outside src/capabilities, src/common and the service folders: the root, deploy, the scripts.</summary>
    public const string Shared = "shared";

    public const string Capabilities = "capabilities";

    public const string Common = "common";

    /// <summary>The prefix of a part that is a project of src/common: each project goes on its own, as a service does.</summary>
    private const string CommonProject = Common + "/";

    /// <summary>
    /// The part a path relative to the root belongs to: its service folder's name, a project of src/common as
    /// common/&lt;project&gt;, src/capabilities, or the shared part.
    /// </summary>
    public static string Of(string path)
    {
        if (Layout.ServiceFolderOf(path) is { } folder)
        {
            return folder[(folder.LastIndexOf('/') + 1)..];
        }

        var segments = path.Split('/');
        return segments switch
        {
            ["src", Capabilities, ..] => Capabilities,
            ["src", Common, var project, _, ..] => CommonProject + project,
            ["src", Common, ..] => Common,
            _ => Shared,
        };
    }

    /// <summary>A part every service uses, which goes before the services: shared, the capabilities, Common and its projects.</summary>
    public static bool IsShared(string part) => part is Shared or Capabilities or Common || part.StartsWith(CommonProject, StringComparison.Ordinal);

    /// <summary>
    /// The parts of a tree in the order they go: shared, Common's own files, Common's projects - a project before
    /// those that reference it, so a type the template moves down to a project others reference arrives there before
    /// it leaves the old one - then the capabilities, which use Common, then the service folders.
    /// </summary>
    /// <param name="paths">The files of the tree, relative to its root.</param>
    /// <param name="references">The parts of Common each part of Common references; none given, Common's projects go by layer.</param>
    public static IReadOnlyList<string> In(IEnumerable<string> paths, IReadOnlyDictionary<string, IReadOnlySet<string>>? references = null)
    {
        var found = paths.Select(Of).Distinct(StringComparer.Ordinal).ToList();
        var projects = found.Where(p => p.StartsWith(CommonProject, StringComparison.Ordinal)).OrderBy(LayerOf).ThenBy(p => p, StringComparer.Ordinal).ToList();
        return [Shared, .. found.Contains(Common) ? [Common] : Array.Empty<string>(), .. DependenciesFirst(projects, references ?? new Dictionary<string, IReadOnlySet<string>>()),
            Capabilities, .. found.Where(p => !IsShared(p)).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The layer of a project of Common by the last segment of its name, from the domain to the tests: what orders
    /// the projects that do not reference each other. A name of no layer goes with the infrastructure.
    /// </summary>
    private static int LayerOf(string part)
    {
        var name = part[(part.LastIndexOf('/') + 1)..];
        var layer = Array.FindIndex(Layers, l => name.EndsWith("." + l, StringComparison.Ordinal));
        return layer >= 0 ? layer : name.EndsWith("." + Common, StringComparison.OrdinalIgnoreCase) ? 0 : Array.IndexOf(Layers, "Infrastructure");
    }

    /// <summary>The layers of Common in the order they go, from the domain to the tests.</summary>
    private static readonly string[] Layers = ["Domain", "Contracts", "Application", "Infrastructure", "Web", "Testing", "Tests"];

    /// <summary>The part of Common a project file of the solution is, when it is one: common/&lt;project&gt;.</summary>
    public static string? CommonPartOf(string projectPath) => Of(projectPath) is var part && part.StartsWith(CommonProject, StringComparison.Ordinal) ? part : null;

    /// <summary>
    /// The parts in an order where each comes after those it references, the first by layer among the ready; parts
    /// that reference each other in a cycle follow by layer.
    /// </summary>
    private static IEnumerable<string> DependenciesFirst(IReadOnlyList<string> parts, IReadOnlyDictionary<string, IReadOnlySet<string>> references)
    {
        var left = new List<string>(parts);
        while (left.Count > 0)
        {
            var ready = left.FirstOrDefault(p => !references.TryGetValue(p, out var uses) || !uses.Any(u => u != p && left.Contains(u))) ?? left[0];
            left.Remove(ready);
            yield return ready;
        }
    }

    /// <summary>
    /// The part this run takes: the one <paramref name="requested"/> names - shared, or a service folder by its full
    /// name or its last segment - else the first not merged. The shared parts go first, in their order: a service's files use them.
    /// </summary>
    public static string Next(IReadOnlyList<string> parts, IReadOnlyCollection<string> done, string? requested)
    {
        var left = parts.Where(p => !done.Contains(p)).ToList();
        if (left.Count == 0)
        {
            throw new InvalidOperationException($"Every part of this upgrade is merged: dotskit upgrade, without --part {requested}, finishes it.");
        }

        if (requested is null)
        {
            return left[0];
        }

        var named = parts.FirstOrDefault(p => string.Equals(p, requested, StringComparison.OrdinalIgnoreCase) || p.EndsWith("." + requested, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"--part {requested} is no part of this upgrade: name one of {string.Join(", ", parts)}.");
        return (done.Contains(named), IsShared(left[0]) && named != left[0]) switch
        {
            (true, _) => throw new InvalidOperationException($"The part {named} is merged already; the parts left: {string.Join(", ", left)}."),
            (_, true) => throw new InvalidOperationException($"The shared parts go first, in order, as the services use them: dotskit upgrade --part {left[0]}."),
            _ => named,
        };
    }
}

/// <summary>A step of an upgrade a part at a time: the parts merged before it and the one it merges.</summary>
/// <param name="Done">The parts merged before this step.</param>
/// <param name="Part">The part this step merges.</param>
/// <param name="Parts">
/// The parts of the upgrade, kept in the manifest from its first run; null at the first run, when the planner takes
/// them from the solution and the template's next output. A path of no part of them goes with the shared part.
/// </param>
internal sealed record PartStep(IReadOnlySet<string> Done, string Part, IReadOnlyList<string>? Parts = null)
{
    public bool InDone(string path) => Done.Contains(PartOf(path));

    public bool InDoneOrThis(string path) => InDone(path) || PartOf(path) == Part;

    private string PartOf(string path) => UpgradeParts.Of(path) is var part && (Parts is null || Parts.Contains(part)) ? part : UpgradeParts.Shared;
}
