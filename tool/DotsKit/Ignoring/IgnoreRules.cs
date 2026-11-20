namespace DotsKit.Ignoring;

/// <summary>
/// The files an upgrade does not touch: those <c>.dotskitignore</c> names and whose pin still holds at the version the
/// upgrade goes to. A path with no pin is left alone until a person removes its rule, as the team wrote it; a pinned
/// path is left alone only while the pin has not expired, and once it has, the path merges as any other.
/// </summary>
internal interface IIgnoreRules
{
    /// <summary>
    /// The candidates, relative to <paramref name="root"/>, an upgrade to <paramref name="to"/> must not write, and the
    /// rules whose pins it spends.
    /// </summary>
    /// <param name="root">The solution's root, where <c>.dotskitignore</c> sits.</param>
    /// <param name="to">The release the upgrade goes to, by which the pins are read.</param>
    /// <param name="candidates">
    /// The paths the upgrade could write, relative to the root with forward slashes - the solution's files and the
    /// template's next output together, so a rule holds a file the template adds as well as one it changes.
    /// </param>
    /// <param name="ct">Cancels the read.</param>
    Task<IgnoreDecision> DecideAsync(string root, string to, IReadOnlyList<string> candidates, CancellationToken ct);
}

/// <summary>
/// What the rules of <c>.dotskitignore</c> do at one version: the paths held, and the rules whose pins it spends - the
/// tool brings both to the upgrade's review, a person confirms.
/// </summary>
/// <param name="Held">The paths the rules hold at <paramref name="Version"/>; no file of them is read, written or merged.</param>
/// <param name="Spent">The rules whose pin expired at <paramref name="Version"/>: their paths merge, and the review names the line.</param>
/// <param name="Version">The release the upgrade goes to, by which the pins are read.</param>
internal sealed record IgnoreDecision(IReadOnlySet<string> Held, IReadOnlyList<IgnoreRule> Spent, string Version)
{
    public static readonly IgnoreDecision None = new(new HashSet<string>(), [], "");
}

/// <summary>
/// Reads <c>.dotskitignore</c> of a folder, hands the patterns git matches with, and keeps the candidates git matches.
/// The match is git's own (<see cref="IIgnorePatterns"/>): a pattern and the path are compared by the syntax the file
/// is written in, so the tool and the team read the file the same way.
/// </summary>
/// <param name="patterns">The matcher the rules go to.</param>
/// <param name="toolVersion">
/// The version the tool is released as, which reads a pin an upgrade to the template's sources is judged by: the sources
/// carry no version of their own, and a pin is renewed onto the tool's line there.
/// </param>
internal sealed class IgnoreRules(IIgnorePatterns patterns, Func<string> toolVersion) : IIgnoreRules
{
    /// <summary>The file's name in the solution's root, beside the manifest.</summary>
    public const string FileName = ".dotskitignore";

    public static string PathOf(string root) => System.IO.Path.Combine(root, FileName);

    public async Task<IgnoreDecision> DecideAsync(string root, string to, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        var skip = DotskitIgnore.Parse(Read(root));
        if (skip.IsEmpty)
        {
            return IgnoreDecision.None with { Version = to };
        }

        // A pin holds until the upgrade reaches it: at the pin's precision or above, the file merges and the review
        // names the spent rule. A rule with no pin holds always, until a person removes it.
        var line = LineOf(to);
        var spent = skip.Rules.Where(r => r.Pin is { } pin && !pin.Holds(line)).ToList();
        var holding = skip.Rules.Where(r => r.Pin is null || r.Pin.Holds(line)).ToList();
        var held = holding.Count == 0
            ? new HashSet<string>()
            : await patterns.MatchAsync(root, [.. holding.Select(r => r.Pattern)], candidates, ct);
        return new IgnoreDecision(held, spent, line);
    }

    /// <summary>The version a pin is read and renewed by: the release an upgrade goes to, or the tool's own for the sources.</summary>
    public string LineOf(string version) => version == Generation.TemplateSource.SourcesVersion ? toolVersion() : version;

    private static string? Read(string root) =>
        File.Exists(PathOf(root)) ? File.ReadAllText(PathOf(root)) : null;
}

/// <summary>
/// The candidates a set of gitignore patterns answers to. git itself does the matching, so the semantics are the
/// file's own: the depth of the folder, a deeper rule over a higher one, a later line over an earlier one, and
/// <c>!</c> cancelling an ignore.
/// </summary>
internal interface IIgnorePatterns
{
    /// <summary>The candidates a pattern matches, relative to <paramref name="root"/> with forward slashes.</summary>
    Task<IReadOnlySet<string>> MatchAsync(string root, IReadOnlyList<string> patterns, IReadOnlyList<string> candidates, CancellationToken ct);
}
