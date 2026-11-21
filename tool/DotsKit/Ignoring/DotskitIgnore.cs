namespace DotsKit.Ignoring;

/// <summary>
/// <c>.dotskitignore</c>, the file that says what an upgrade does not touch. The rules are gitignore's own: a path
/// or a pattern, a deeper rule over a higher one, a later line over an earlier one, and <c>!</c> cancels an ignore. A
/// rule carries an optional pin (<see cref="Pin"/>), which holds the file against the upgrades below its precision.
/// The file sits beside the manifest in the solution's root and in any folder below it, as a <c>.gitignore</c> does;
/// a rule acts on the subtree of the folder its file sits in, and a deeper file overrides a higher one.
/// </summary>
/// <param name="Source">The folder of the file, relative to the solution's root with forward slashes; empty at the root.</param>
/// <param name="Rules">The rules of the file, in the order they stand; a blank and a comment are no rule.</param>
/// <param name="Text">The file as it is written, so a rewrite leaves every line the tool did not touch as the team wrote it.</param>
internal sealed record DotskitIgnore(string Source, IReadOnlyList<IgnoreRule> Rules, string Text)
{
    /// <summary>
    /// The file of one folder, read as text; a folder with no file and an empty file both have no rules.
    /// </summary>
    /// <param name="source">The folder of the file, relative to the solution's root; empty at the root.</param>
    /// <param name="text">The file as read.</param>
    public static DotskitIgnore Parse(string source, string? text)
    {
        var file = (text ?? "").ReplaceLineEndings("\n");
        return new DotskitIgnore(
            source,
            [.. file.Split('\n').Select((line, number) => IgnoreRule.Parse(line, number + 1, source)).OfType<IgnoreRule>()],
            file);
    }

    public bool IsEmpty => Rules.Count == 0;

    /// <summary>The file itself, relative to the solution's root, as the tool writes it and git stages it.</summary>
    public string PathIn(string root) => Path.GetRelativePath(root, DotskitIgnores.PathOf(root, Source)).Replace('\\', '/');
}

/// <summary>
/// The files of <c>.dotskitignore</c> a solution holds, the root's first and the deeper ones after it: a rule acts on
/// the subtree of the folder its file sits in, and a deeper file overrides a higher one, as <c>.gitignore</c> does.
/// </summary>
internal static class DotskitIgnores
{
    /// <summary>The file's name in any folder.</summary>
    public const string FileName = ".dotskitignore";

    /// <summary>The file of a folder, by full path.</summary>
    public static string PathOf(string root, string source = "") =>
        source.Length == 0 ? Path.Combine(root, FileName) : Path.Combine(root, source, FileName);

    /// <summary>
    /// Every file of the solution's tree, the root's first if it has one, the deeper ones by their depth then their
    /// path; a file with no rules is left out. The folders <see cref="Layout.Ignored"/> names hold no file of them.
    /// </summary>
    public static IReadOnlyList<DotskitIgnore> Load(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return [.. Io.Folders.FilesOf(root)
            .Where(f => Path.GetFileName(f) == FileName)
            .Select(f => Path.GetRelativePath(root, Path.GetDirectoryName(f)!).Replace('\\', '/'))
            .Select(source => source == "." ? "" : source)
            .OrderBy(source => source.Length == 0 ? 0 : source.Count(c => c == '/') + 1)
            .ThenBy(source => source, StringComparer.Ordinal)
            .Select(source => ParseSource(root, source))
            .Where(f => !f.IsEmpty)];
    }

    private static DotskitIgnore ParseSource(string root, string source) =>
        DotskitIgnore.Parse(source, File.Exists(PathOf(root, source)) ? File.ReadAllText(PathOf(root, source)) : null);
}

/// <summary>
/// One rule of <c>.dotskitignore</c>: the pattern git matches a path with, the pin that holds it, the line it came
/// from and the file it sits in, so a change the tool makes names the line a person reads.
/// </summary>
/// <param name="Pattern">The path or pattern as git reads it, without the pin; a line starting with <c>!</c> keeps it.</param>
/// <param name="Pin">The version the rule holds to; null for a rule that holds nothing and merges as git does.</param>
/// <param name="Source">The folder of the file the rule sits in, relative to the solution's root; empty at the root.</param>
/// <param name="Line">The line of the file, from one.</param>
internal sealed record IgnoreRule(string Pattern, Pin? Pin, string Source, int Line)
{
    private const char Comment = '#';
    private const char PinMark = '@';

    /// <summary>A rule from a line of the file, or null for a blank and for a comment.</summary>
    public static IgnoreRule? Parse(string line, int number, string source)
    {
        var text = line.TrimEnd();
        if (text.TrimStart() is [] or [Comment, ..])
        {
            return null;
        }

        // A pin is the suffix of a pattern: the last @ separates the two. A pattern whose suffix is no pin - an @ in a
        // path, say - stays whole: the tool reads no pin and leaves the line as the team wrote it.
        var at = text.LastIndexOf(PinMark);
        return at > 0 && Pin.Parse(text[(at + 1)..]) is { } pin
            ? new IgnoreRule(text[..at], pin, source, number)
            : new IgnoreRule(text, null, source, number);
    }

    /// <summary>The file the rule sits in, relative to the solution's root, as a person names it.</summary>
    public string FilePath => Source.Length == 0 ? DotskitIgnores.FileName : $"{Source}/{DotskitIgnores.FileName}";
}
