namespace DotsKit.Merging;

/// <summary>
/// The style of the template united with the team's own: the template's rules, then the solution's root
/// <c>.editorconfig</c> after them, so the team's rule wins where both name one - the editorconfig rule that the file
/// nearer the source wins. The union never lands in the solution: it is written into the folder the tool formatted the
/// template's side in, and that side is what the merge takes.
/// </summary>
internal static class StyleUnion
{
    /// <summary>The file a folder of the tool's own is formatted by.</summary>
    public const string FileName = ".editorconfig";

    /// <summary>The union of <paramref name="template"/> and <paramref name="team"/>; the team's rules come last and win.</summary>
    /// <param name="template">The template's style, as the package carries it.</param>
    /// <param name="team">The solution's root .editorconfig, or null when it has none.</param>
    public static string Of(string template, string? team)
    {
        var text = LineEndings.ToLf(template).TrimEnd('\n');
        if (string.IsNullOrWhiteSpace(team))
        {
            return text + "\n";
        }

        // One root for one tree, and the template's already states it. The team's file states the same line; a second
        // one is no rule of the union.
        var theirs = string.Join('\n', LineEndings.ToLf(team).TrimEnd('\n').Split('\n')
            .Where(line => !line.TrimStart().StartsWith("root", StringComparison.Ordinal)));
        return text + "\n\n" + theirs.TrimStart('\n').TrimEnd('\n') + "\n";
    }
}
