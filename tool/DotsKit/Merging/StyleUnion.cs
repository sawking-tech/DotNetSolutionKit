namespace DotsKit.Merging;

/// <summary>
/// The style of the template united with the team's own: the template's rules, then the solution's root
/// <c>.editorconfig</c> after them, so the team's rule wins where both name one - the editorconfig rule that the file
/// nearer the source wins.
///
/// The union changes no value and no severity. Every line of both files goes in as it is written, but the second
/// <c>root</c>: one tree has one root, and the template's line already states it. That is the whole of the union,
/// because the formatter that applies it - <c>dotnet format whitespace</c> - reads the options, not the severities:
/// an option at <c>suggestion</c> is applied for its value, and a raised severity buys nothing.
///
/// Raising a severity is worse than useless here. A layout option such as <c>csharp_new_line_before_open_brace =
/// all</c> takes a list of values and no <c>:severity</c> suffix; appending one gives a value the formatter cannot
/// read, and the invalid line, being later, overrides the template's valid line above and turns the layout off.
/// A raised severity on an analyzer rule is the other harm: <c>dotnet format style</c> then applies an analyzer fix,
/// and a fix rewrites the code - a namespace to its block form, an <c>if</c> to a conditional expression - which a
/// merge that lays code out must never do.
///
/// The cost is stated: the usings' order and the namespace declaration are <c>style</c> rules, and the merge leaves
/// them as the template wrote them. A team whose usings are sorted otherwise sees those lines conflict; the code
/// itself arrives correct and unrewritten, which is the invariant that matters more.
/// </summary>
internal static class StyleUnion
{
    /// <summary>The file a folder of the tool's own is formatted by.</summary>
    public const string FileName = ".editorconfig";

    /// <summary>The union of <paramref name="template"/> and <paramref name="team"/>; the team's rules come last and win.</summary>
    /// <param name="template">The template's style, as the repository holds it.</param>
    /// <param name="team">The solution's root .editorconfig; null when it has none.</param>
    public static string Of(string template, string? team)
    {
        var text = LineEndings.ToLf(template).TrimEnd('\n');
        if (string.IsNullOrWhiteSpace(team))
        {
            return text + "\n";
        }

        // One root for one tree, and the template's already states it. The team's file states the same line; a second
        // one is no rule of the union. Every other line is theirs to state, and goes in as written.
        var theirs = LineEndings.ToLf(team).TrimEnd('\n').Split('\n')
            .Where(line => !line.TrimStart().StartsWith("root", StringComparison.Ordinal));
        return text + "\n\n" + string.Join('\n', theirs).TrimStart('\n').TrimEnd('\n') + "\n";
    }
}
