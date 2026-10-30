using System.Text.RegularExpressions;

namespace DotsKit.Solutions;

/// <summary>
/// The ProjectReference items of a project, added and removed in its text. No MSBuild evaluates the project, so a
/// Directory.Build.props left in conflict by a merge, which MSBuild cannot read, stops nothing; the line keeps the
/// form <c>dotnet add reference</c> writes, a path relative to the project with back slashes.
/// </summary>
internal static partial class ProjectReferences
{
    private const string ItemGroupEnd = "</ItemGroup>";
    private const string ProjectEnd = "</Project>";

    [GeneratedRegex(@"[ \t]*<ProjectReference\b[^>]*?\sInclude=""(?<path>[^""]+)""[^>]*/>[ \t]*\r?\n?")]
    private static partial Regex Item();

    /// <summary>The text with a reference to <paramref name="referenced"/>; the same text when it has one.</summary>
    public static string Add(string text, string project, string referenced)
    {
        if (Item().Matches(text).Any(m => Same(project, m.Groups["path"].Value, referenced)))
        {
            return text;
        }

        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var line = $"    <ProjectReference Include=\"{Path.GetRelativePath(Path.GetDirectoryName(project)!, referenced).Replace('/', '\\')}\" />";
        // Beside the other references when there are some, else in an item group of its own before the end.
        if (Item().Matches(text).LastOrDefault() is { } last)
        {
            var at = last.Index + last.Length;
            var indent = last.Value[..(last.Value.Length - last.Value.TrimStart(' ', '\t').Length)];
            return last.Value.EndsWith('\n')
                ? text.Insert(at, indent + line.TrimStart() + newLine)
                : text.Insert(at, newLine + indent + line.TrimStart());
        }

        var end = text.LastIndexOf(ProjectEnd, StringComparison.Ordinal);
        return end < 0 ? text : text.Insert(end, $"  <ItemGroup>{newLine}{line}{newLine}  {ItemGroupEnd}{newLine}");
    }

    /// <summary>The text without its reference to <paramref name="referenced"/>; the same text when it has none.</summary>
    public static string Remove(string text, string project, string referenced) =>
        Item().Replace(text, m => Same(project, m.Groups["path"].Value, referenced) ? "" : m.Value);

    private static bool Same(string project, string include, string referenced) =>
        string.Equals(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include.Replace('\\', '/'))), Path.GetFullPath(referenced),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
