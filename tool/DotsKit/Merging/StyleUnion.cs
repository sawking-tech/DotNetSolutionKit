namespace DotsKit.Merging;

/// <summary>
/// The template's style united with the team's own: the template's rules, then the solution's root
/// <c>.editorconfig</c> after them, so the team's rule wins where both name one. No line changes but the second
/// <c>root</c>; the formatter is given the severity to apply, so no value in either file is rewritten.
/// </summary>
internal static class StyleUnion
{
    /// <summary>The file a folder of the tool's own is formatted by.</summary>
    public const string FileName = ".editorconfig";

    /// <summary>The union of <paramref name="template"/> and <paramref name="team"/>, the team's rules last and winning.</summary>
    /// <param name="template">The template's style, as the repository holds it.</param>
    /// <param name="team">The solution's root .editorconfig; null when it has none.</param>
    public static string Of(string template, string? team)
    {
        var text = LineEndings.ToLf(template).TrimEnd('\n');
        if (string.IsNullOrWhiteSpace(team))
        {
            return text + "\n";
        }

        // One tree has one root, and the template's line states it.
        var theirs = LineEndings.ToLf(team).TrimEnd('\n').Split('\n')
            .Where(line => !line.TrimStart().StartsWith("root", StringComparison.Ordinal));
        return text + "\n\n" + string.Join('\n', theirs).TrimStart('\n').TrimEnd('\n') + "\n";
    }
}
