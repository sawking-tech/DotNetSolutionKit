namespace DotsKit.Ignoring;

/// <summary>
/// <c>.dotskitignore</c>, the file that says what an upgrade does not touch. The rules are gitignore's own: a path
/// or a pattern, a deeper rule over a higher one, a later line over an earlier one, and <c>!</c> cancels an ignore. A
/// rule carries an optional pin (<see cref="Pin"/>), which holds the file against the upgrades below its precision.
/// The file lives in the solution's root, beside the manifest, so the merge never brings it and the team edits it as
/// its own.
/// </summary>
/// <param name="Rules">The rules of the file, in the order they stand; a blank and a comment are no rule.</param>
/// <param name="Text">The file as it is written, so a rewrite leaves every line the tool did not touch as the team wrote it.</param>
internal sealed record DotskitIgnore(IReadOnlyList<IgnoreRule> Rules, string Text)
{
    /// <summary>The rules of the file, read as text; a folder with no file and an empty file both have no rules.</summary>
    public static DotskitIgnore Parse(string? text)
    {
        var file = (text ?? "").ReplaceLineEndings("\n");
        return new DotskitIgnore(
            [.. file.Split('\n').Select((line, number) => IgnoreRule.Parse(line, number + 1)).OfType<IgnoreRule>()],
            file);
    }

    public bool IsEmpty => Rules.Count == 0;

    /// <summary>The patterns in the words git reads, a rule whose path is held or spent alike: the pin is not part of a pattern.</summary>
    public IEnumerable<string> Patterns => Rules.Select(r => r.Pattern);
}

/// <summary>
/// One rule of <c>.dotskitignore</c>: the pattern git matches a path with, the pin that holds it, and the line it came
/// from, so a change the tool makes names the line a person reads.
/// </summary>
/// <param name="Pattern">The path or pattern as git reads it, without the pin; a line starting with <c>!</c> keeps it.</param>
/// <param name="Pin">The version the rule holds to; null for a rule that holds nothing and merges as git does.</param>
/// <param name="Line">The line of the file, from one.</param>
internal sealed record IgnoreRule(string Pattern, Pin? Pin, int Line)
{
    private const char Comment = '#';
    private const char PinMark = '@';

    /// <summary>A rule from a line of the file, or null for a blank and for a comment.</summary>
    public static IgnoreRule? Parse(string line, int number)
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
            ? new IgnoreRule(text[..at], pin, number)
            : new IgnoreRule(text, null, number);
    }
}
