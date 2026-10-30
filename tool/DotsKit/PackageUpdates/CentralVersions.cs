using System.Text.RegularExpressions;
using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.Solutions;

namespace DotsKit.PackageUpdates;

/// <summary>
/// Central package management is the template's frame: in a solution whose src/Directory.Packages.props manages the
/// versions, a PackageReference of the team's that still names a version gives it to the central file and drops it,
/// so NuGet does not refuse the project (NU1008). A version the central file has already stays - the template's, or
/// the one of the first project by path, or a flag's of src/package-versions - so one package has one version. A
/// project left in conflict, one of a part not merged yet and one central management is turned off for are left alone.
/// </summary>
internal static partial class CentralVersions
{
    private const string Central = "src/Directory.Packages.props";
    private const string Managed = "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>";
    private const string ItemGroupEnd = "</ItemGroup>";
    private const string Source = "src/";
    private const string OptedOut = "<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>";

    [GeneratedRegex("""(?<head><PackageReference\s+Include="(?<name>[^"]+)")\s+Version="(?<version>[^"]+)"(?<tail>[^>]*>)""")]
    private static partial Regex Versioned();

    /// <summary>
    /// The versions to move, a repair each. Only a project under src, which the central file governs, and not one
    /// that opts out of central management; nothing while the central file itself is in conflict.
    /// </summary>
    /// <param name="root">The solution's root.</param>
    /// <param name="conflicts">The files left in conflict: neither they nor the central file in conflict are touched.</param>
    /// <param name="waiting">The template's files of the parts not merged yet: their projects wait for their part, which brings them as the template has them.</param>
    public static IReadOnlyList<Repair> Decide(string root, IReadOnlyCollection<string> conflicts, IReadOnlySet<string>? waiting = null)
    {
        var central = FileText.Read(root, Central);
        if (central is null || conflicts.Contains(Central) || !central.Text.Contains(Managed, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var centralPath = Path.GetFullPath(Path.Combine(root, Central));
        // The versions of every file of central management: a flag's file of src/package-versions declares its packages.
        var declared = PackageVersions.All(root);
        return [.. Folders.RelativeFiles(root)
            .Where(f => f.StartsWith(Source, StringComparison.Ordinal) && f.EndsWith(Layout.ProjectExtension, StringComparison.OrdinalIgnoreCase)
                && !conflicts.Contains(f) && waiting?.Contains(f) != true)
            .Order(StringComparer.Ordinal)
            .Select(f => (Path: f, Text: FileText.Read(root, f)?.Text ?? ""))
            .Where(f => !f.Text.Contains(OptedOut, StringComparison.OrdinalIgnoreCase) && !OptsOutAbove(root, f.Path))
            .SelectMany(f => Versioned().Matches(f.Text)
                .Select(m => new CentralizeVersion(Path.GetFullPath(Path.Combine(root, f.Path)), m.Groups["name"].Value, m.Groups["version"].Value, centralPath,
                    declared.GetValueOrDefault(m.Groups["name"].Value))))];
    }

    /// <summary>
    /// The project drops the version of the package when the central file has none or the same, and the central file
    /// gets it; a different version stays as the project's VersionOverride, so no package changes its version.
    /// Returns whether a file changed.
    /// </summary>
    public static bool Apply(CentralizeVersion repair)
    {
        var central = FileText.Read(Path.GetDirectoryName(repair.Central), Path.GetFileName(repair.Central));
        var project = FileText.Read(Path.GetDirectoryName(repair.Project), Path.GetFileName(repair.Project));
        if (central is null || project is null || central.IsBinary || project.IsBinary)
        {
            return false;
        }

        // The central file as it is now, after the projects before this one, else the version a flag's file declared.
        var has = PackageVersions.Read(central.Text).GetValueOrDefault(repair.Package) ?? repair.Kept;
        if (has is null)
        {
            Folders.WriteWhole(repair.Central, (central with { Text = Insert(central.Text, repair.Package, repair.Version) }).ToBytes());
        }

        var overrides = has is not null && has != repair.Version;
        var moved = Versioned().Replace(project.Text, m => !string.Equals(m.Groups["name"].Value, repair.Package, StringComparison.OrdinalIgnoreCase)
            ? m.Value
            : overrides ? $"{m.Groups["head"].Value} VersionOverride=\"{m.Groups["version"].Value}\"{m.Groups["tail"].Value}" : m.Groups["head"].Value + m.Groups["tail"].Value);
        Folders.WriteWhole(repair.Project, (project with { Text = moved }).ToBytes());
        return true;
    }

    /// <summary>
    /// A Directory.Build.props between the project and the root turns central management off for its folder: the
    /// projects under it keep their versions, which the central file would not give them.
    /// </summary>
    private static bool OptsOutAbove(string root, string project)
    {
        for (var folder = Path.GetDirectoryName(project.Replace('/', Path.DirectorySeparatorChar)); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            if (FileText.Read(root, Path.Combine(folder, BuildProps))?.Text.Contains(OptedOut, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }

    private const string BuildProps = "Directory.Build.props";

    private static string Insert(string text, string package, string version)
    {
        var end = text.LastIndexOf(ItemGroupEnd, StringComparison.Ordinal);
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lineStart = text.LastIndexOf('\n', Math.Max(end, 0)) + 1;
        return end < 0 ? text : text.Insert(lineStart, $"    <PackageVersion Include=\"{package}\" Version=\"{version}\" />{newLine}");
    }
}
