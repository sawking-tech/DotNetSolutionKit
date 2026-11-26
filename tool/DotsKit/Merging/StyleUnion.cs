namespace DotsKit.Merging;

/// <summary>
/// The style of the template united with the team's own: the template's rules, then the solution's root
/// <c>.editorconfig</c> after them, so the team's rule wins where both name one - the editorconfig rule that the file
/// nearer the source wins.
///
/// One thing of the team's file does not carry into the union: the severity. The file arrives from the template with
/// every rule at <c>suggestion</c>, which says a build of theirs does not fail on our style - a statement about their
/// build, not about the layout they want. The merge lays the template's side out by their rules, so a rule they hold
/// at <c>suggestion</c> is raised to <c>warning</c>: the formatter applies it. A rule they set to <c>none</c> stays
/// off - that is them saying they do not want it - and a rule they deleted is replaced by the template's line above,
/// which is what 2.9 did with every style rule.
/// </summary>
internal static class StyleUnion
{
    /// <summary>The file a folder of the tool's own is formatted by.</summary>
    public const string FileName = ".editorconfig";

    private const string Suggestion = "suggestion";
    private const string FormatterApplies = "warning";

    /// <summary>The union of <paramref name="template"/> and <paramref name="team"/>; the team's rules come last and win.</summary>
    /// <param name="template">The template's style, at the severities the repository holds.</param>
    /// <param name="team">The solution's root .editorconfig; null when it has none.</param>
    public static string Of(string template, string? team)
    {
        var text = LineEndings.ToLf(template).TrimEnd('\n');
        if (string.IsNullOrWhiteSpace(team))
        {
            return text + "\n";
        }

        // One root for one tree, and the template's already states it. The team's file states the same line; a second
        // one is no rule of the union.
        var theirs = LineEndings.ToLf(team).TrimEnd('\n').Split('\n')
            .Where(line => !line.TrimStart().StartsWith("root", StringComparison.Ordinal))
            .Select(Raised);
        return text + "\n\n" + string.Join('\n', theirs).TrimStart('\n').TrimEnd('\n') + "\n";
    }

    /// <summary>
    /// A rule the team offered becomes one the formatter applies; one they turned off stays off. A rule that states a
    /// value and no severity - <c>csharp_new_line_before_open_brace = none</c> - is the formatter's by its kind, and it
    /// gets the severity that makes the formatter read it: a value alone is a suggestion, and the layout the team asked
    /// for would never reach the template's side.
    /// </summary>
    private static string Raised(string line)
    {
        var body = line.TrimStart();
        if (body.StartsWith('#') || body.StartsWith('[') || !body.Contains('=', StringComparison.Ordinal))
        {
            return line;
        }

        // The severity sits after the value's colon or as the value itself: csharp_prefer_braces = true:suggestion,
        // dotnet_diagnostic.IDE0011.severity = suggestion.
        var at = line.LastIndexOf(Suggestion, StringComparison.Ordinal);
        if (at >= 0)
        {
            return line[..at] + FormatterApplies + line[(at + Suggestion.Length)..];
        }

        // A value the formatter reads as a suggestion without a word: the layout options and the token spacing. A rule
        // that only steers what an IDE generates is left alone - there is nothing for the formatter to fix in it.
        var name = body[..body.IndexOf('=')].Trim();
        return FormatterReads(name) ? line.TrimEnd() + ":warning" : line;
    }

    /// <summary>
    /// Whether a rule with a value and no severity is one <c>dotnet format style</c> applies. The names are the
    /// options of the formatter: the layout of a brace, the indent, the spacing, the usings and the namespace.
    /// </summary>
    private static bool FormatterReads(string rule) =>
        rule.StartsWith("csharp_new_line_", StringComparison.Ordinal)
        || rule.StartsWith("csharp_indent_", StringComparison.Ordinal)
        || rule.StartsWith("csharp_space_", StringComparison.Ordinal)
        || rule.StartsWith("csharp_preserve_single_line", StringComparison.Ordinal)
        || rule is "csharp_prefer_braces" or "csharp_using_directive_placement" or "csharp_style_namespace_declarations"
        || rule.StartsWith("dotnet_style_", StringComparison.Ordinal)
        || rule is "dotnet_sort_system_directives_first" or "dotnet_separate_import_directive_groups";
}
