using System.Text.RegularExpressions;
using DotsKit.Merging;

namespace DotsKit.Solutions;

/// <summary>
/// The using lines of a C# file, changed as text: a namespace is added after the last using at the top, or above
/// the first line when there is none; a using of a namespace, global or not, is replaced in place. A namespace a
/// file uses already is not added twice. The file's line endings are kept.
/// </summary>
internal static partial class UsingLines
{
    [GeneratedRegex(@"^\s*(global\s+)?using\s+(static\s+)?[\w.]+\s*;\s*$")]
    private static partial Regex AnyUsing();

    [GeneratedRegex(@"^\s*(global\s+)?using\s+(?<ns>[\w.]+)\s*;\s*$")]
    private static partial Regex NamespaceUsing();

    [GeneratedRegex(@"^\s*namespace\s+(?<ns>[\w.]+)\s*[;{]?\s*$")]
    private static partial Regex NamespaceDeclaration();

    /// <summary>The namespaces a file has a using of, global ones too; an alias or a static using is not one.</summary>
    public static IReadOnlyList<string> Of(string text) =>
        [.. LineEndings.ToLf(text).Split('\n').Select(NamespaceOf).OfType<string>()];

    /// <summary>The namespaces a file declares, file-scoped or in a block.</summary>
    public static IReadOnlyList<string> Declared(string text) =>
        [.. LineEndings.ToLf(text).Split('\n').Select(l => NamespaceDeclaration().Match(l)).Where(m => m.Success).Select(m => m.Groups["ns"].Value)];

    public static string Add(string text, string ns)
    {
        var newline = LineEndings.Of(text);
        var lines = LineEndings.ToLf(text).Split('\n').ToList();
        if (Uses(lines, ns))
            return text;
        var last = lines.FindLastIndex(l => AnyUsing().IsMatch(l));
        lines.Insert(last + 1, $"using {ns};");
        return LineEndings.Apply(string.Join('\n', lines), newline);
    }

    public static string Replace(string text, string old, string ns)
    {
        var newline = LineEndings.Of(text);
        var lines = LineEndings.ToLf(text).Split('\n').ToList();
        var at = lines.FindIndex(l => NamespaceOf(l) == old);
        if (at < 0)
            return Add(text, ns);
        if (Uses(lines, ns))
            lines.RemoveAt(at);
        else
            lines[at] = lines[at].Replace(old, ns, StringComparison.Ordinal);
        return LineEndings.Apply(string.Join('\n', lines), newline);
    }

    private static string? NamespaceOf(string line) => NamespaceUsing().Match(line) is { Success: true } m ? m.Groups["ns"].Value : null;

    private static bool Uses(List<string> lines, string ns) => lines.Any(l => NamespaceOf(l) == ns);
}
