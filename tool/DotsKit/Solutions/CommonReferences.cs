using System.Text.RegularExpressions;
using DotsKit.Planning;

namespace DotsKit.Solutions;

/// <summary>
/// The parts of Common each project of src/common references, read from its ProjectReference items: the order of
/// an upgrade a part at a time puts a project before those that use it (<see cref="UpgradeParts.In"/>).
/// </summary>
internal static partial class CommonReferences
{
    [GeneratedRegex(@"<ProjectReference\b[^>]*?\sInclude=""(?<path>[^""]+)""")]
    private static partial Regex Reference();

    /// <param name="root">The solution's root.</param>
    /// <param name="files">Its files, relative to the root.</param>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Read(string root, IEnumerable<string> files) =>
        files.Where(f => f.EndsWith(Layout.ProjectExtension, StringComparison.OrdinalIgnoreCase) && UpgradeParts.CommonPartOf(f) is not null)
            .GroupBy(f => UpgradeParts.CommonPartOf(f)!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.SelectMany(f => ReferencedParts(root, f)).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

    private static IEnumerable<string> ReferencedParts(string root, string project) =>
        Reference().Matches(File.ReadAllText(Path.Combine(root, project)))
            .Select(m => Path.GetFullPath(Path.Combine(root, Path.GetDirectoryName(project)!, m.Groups["path"].Value.Replace('\\', '/'))))
            .Select(full => UpgradeParts.CommonPartOf(Path.GetRelativePath(root, full).Replace('\\', '/')))
            .OfType<string>();
}
