namespace DotsKit.Merging;

/// <summary>
/// The bytes the template's files have since 2.9 (.editorconfig of the repository): no byte order mark but in a
/// solution file, no spaces at the ends of lines, a line end at the end. Both merges bring the three sides of a file
/// to it before they merge, so a change of the template's style alone conflicts with nothing the team wrote, and the
/// merged file has the style. A file keeps its own line ends, as a checkout with core.autocrlf writes them; a binary
/// file and a solution file stay as they are.
/// </summary>
internal static class TemplateStyle
{
    private const string WithBomAndTabs = ".sln";

    public static FileText Of(string path, FileText file)
    {
        if (file.IsBinary || path.EndsWith(WithBomAndTabs, StringComparison.OrdinalIgnoreCase))
            return file;
        return new FileText(LineEndings.Apply(Normalized(LineEndings.ToLf(file.Text)), LineEndings.Of(file.Text)), Bom: false);
    }

    /// <summary>An LF text with no spaces at the ends of its lines and one line end at the end; an empty one stays empty.</summary>
    private static string Normalized(string text)
    {
        if (text.Length == 0)
            return text;
        var lines = text.Split('\n').Select(line => line.TrimEnd(' ', '\t')).ToList();
        if (lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines) + "\n";
    }
}
