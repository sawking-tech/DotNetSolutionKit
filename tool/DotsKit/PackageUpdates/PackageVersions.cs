using System.Text.RegularExpressions;
using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.PackageUpdates;

/// <summary>
/// The package versions of a generated tree, in the files of central package management: src/Directory.Packages.props
/// and the files of src/package-versions it imports. Each version is a line <c>&lt;PackageVersion Include="X" Version="Y" /&gt;</c>,
/// or <c>Update="X"</c> where a flag's file moves the version of a package another file declares.
/// </summary>
internal static partial class PackageVersions
{
    private const string Central = "src/Directory.Packages.props";
    private const string PerFlag = "src/package-versions/";
    private const string ItemGroupEnd = "</ItemGroup>";

    [GeneratedRegex("""(?<head><PackageVersion\s+(?:Include|Update)="(?<name>[^"]+)"\s+Version=")(?<version>[^"]+)(?<tail>")""")]
    private static partial Regex Line();

    public static bool IsVersionsFile(string path) =>
        path == Central || path == RootCentral || (path.StartsWith(PerFlag, StringComparison.Ordinal) && path.EndsWith(".props", StringComparison.Ordinal));

    /// <summary>
    /// Central package management at the root of a solution, where MSBuild also looks and where the template kept it
    /// before 2.0: a solution of 1.x declares its versions there, and they are the team's to keep.
    /// </summary>
    private const string RootCentral = "Directory.Packages.props";

    /// <summary>Every package of a text with its version.</summary>
    public static Dictionary<string, string> Read(string text) =>
        Line().Matches(text).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["version"].Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>The text with each package's version as <paramref name="versionOf"/> gives it.</summary>
    public static string Rewrite(string text, Func<string, string, string> versionOf) =>
        Line().Replace(text, m => m.Groups["head"].Value + versionOf(m.Groups["name"].Value, m.Groups["version"].Value) + m.Groups["tail"].Value);

    /// <summary>
    /// Puts the policy of the solution into what the template gives now, before the merge: a pinned version, and a
    /// version the policy holds back, are written in the template's files, so these lines never conflict with the
    /// solution's. Returns what the report says about each package that changes or is held.
    /// </summary>
    /// <param name="baseDir">What the template gave before; null for a new solution.</param>
    /// <param name="nextDir">What the template gives now; its version files are rewritten in place.</param>
    /// <param name="policy">The solution's policy: the pinned versions and how far an update goes.</param>
    /// <param name="majorUpgrade">The template moves to its next major version: a package's major version may come with it.</param>
    public static IReadOnlyList<string> Hold(string? baseDir, string nextDir, PackagePolicy policy, bool majorUpgrade)
    {
        var had = baseDir is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : All(baseDir);
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        var brought = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Folders.RelativeFiles(nextDir).Where(IsVersionsFile))
        {
            // Through FileText, so the byte order mark the template gives stays.
            var file = FileText.Read(nextDir, path)!;
            var rewritten = Rewrite(file.Text, (name, version) =>
            {
                brought.Add(name);
                var decision = policy.Decide(name, had.GetValueOrDefault(name), version, majorUpgrade);
                if (decision.Line is not null)
                {
                    lines.Add($"{name}: {decision.Line}");
                }

                return decision.Version;
            });
            if (rewritten != file.Text)
            {
                File.WriteAllBytes(Path.Combine(nextDir, path), (file with { Text = rewritten }).ToBytes());
            }
        }
        // A pin of a package the template does not have - or of a misspelt one - pins nothing, and says so.
        foreach (var (name, version) in policy.Pinned.Where(p => !brought.Contains(p.Key)))
        {
            lines.Add($"{name}: pinned on {version}, not among the template's packages");
        }

        return [.. lines];
    }

    /// <summary>
    /// A step of an upgrade a part at a time: a package the template no longer declares keeps its version from
    /// <paramref name="before"/> while a project of the tree still references it - the project of a part not merged
    /// yet - so the shared part does not break the parts after it; the part that drops the last reference drops it.
    /// The two trees of a step are kept alike, so the next step's base is this step's next tree.
    /// </summary>
    /// <param name="tree">A tree of the template's output; its Directory.Packages.props is written in place.</param>
    /// <param name="before">The template's whole output before the upgrade, where a kept version is read.</param>
    /// <param name="solution">
    /// The solution's root: a project of the team's that references a package the template drops keeps the package's
    /// version too, as the merge would otherwise take the line the team never touched out of the central file; a
    /// package of the team's own, which no output of the template declares, keeps its line the same way.
    /// </param>
    public static void KeepReferenced(string tree, string before, string solution)
    {
        var central = FileText.Read(tree, Central);
        if (central is null)
        {
            return;
        }

        var declared = All(tree);
        var had = All(before);
        // The solution's own packages: a team's PackageVersion line sits in a block the template rewrites, and a
        // merge resolved to the template's side would take it along (NU1010). The template's own last version wins.
        foreach (var (name, version) in All(solution))
        {
            had.TryAdd(name, version);
        }

        var kept = ReferencedIn(tree).Concat(ReferencedIn(solution))
            .Where(name => !declared.ContainsKey(name) && had.ContainsKey(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var end = central.Text.LastIndexOf(ItemGroupEnd, StringComparison.Ordinal);
        if (kept.Count == 0 || end < 0)
        {
            return;
        }

        var newLine = central.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = string.Concat(kept.Select(name => $"    <PackageVersion Include=\"{name}\" Version=\"{had[name]}\" />{newLine}"));
        var lineStart = central.Text.LastIndexOf('\n', end) + 1;
        File.WriteAllBytes(Path.Combine(tree, Central), (central with { Text = central.Text.Insert(lineStart, lines) }).ToBytes());
    }

    [GeneratedRegex("""<PackageReference\b[^>]*?\sInclude="(?<name>[^"]+)""")]
    private static partial Regex Reference();

    /// <summary>The packages the projects and props of a tree reference.</summary>
    private static IEnumerable<string> ReferencedIn(string root) =>
        Folders.RelativeFiles(root)
            .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => Reference().Matches(FileText.Read(root, p)?.Text ?? "").Select(m => m.Groups["name"].Value));

    /// <summary>Every package the version files of a tree declare, the central file and each flag's, with its version.</summary>
    public static Dictionary<string, string> All(string root)
    {
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Folders.RelativeFiles(root).Where(IsVersionsFile))
        {
            foreach (var (name, version) in Read(FileText.Read(root, path)!.Text))
            {
                all[name] = version;
            }
        }

        return all;
    }
}
