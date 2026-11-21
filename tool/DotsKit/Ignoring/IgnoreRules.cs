namespace DotsKit.Ignoring;

/// <summary>
/// The files an upgrade does not touch: those a <c>.dotskitignore</c> names and whose pin still holds at the version the
/// upgrade goes to. A path with no pin is left alone until a person removes its rule, as the team wrote it; a pinned
/// path is left alone only while the pin has not expired, and once it has, the path merges as any other.
/// </summary>
internal interface IIgnoreRules
{
    /// <summary>
    /// The candidates, relative to <paramref name="root"/>, an upgrade to <paramref name="to"/> must not write, and the
    /// rules whose pins it spends.
    /// </summary>
    /// <param name="root">The solution's root, where the root <c>.dotskitignore</c> sits.</param>
    /// <param name="to">The release the upgrade goes to, by which the pins are read; a release, not the sources.</param>
    /// <param name="from">The release the solution is on, which a pin of the whole major is read against.</param>
    /// <param name="candidates">
    /// The paths the upgrade could write, relative to the root with forward slashes - the solution's files and the
    /// template's next output together, so a rule holds a file the template adds as well as one it changes.
    /// </param>
    /// <param name="ct">Cancels the read.</param>
    Task<IgnoreDecision> DecideAsync(string root, string to, string from, IReadOnlyList<string> candidates, CancellationToken ct);
}

/// <summary>
/// What the rules of <c>.dotskitignore</c> do at one version: the paths held, and the rules whose pins it spends - the
/// tool brings both to the upgrade's review, a person confirms.
/// </summary>
/// <param name="Held">The paths the rules hold at <paramref name="Version"/>; no file of them is read, written or merged.</param>
/// <param name="Spent">The rules whose pin expired at <paramref name="Version"/>: their paths merge, and the review names the line.</param>
/// <param name="Version">The release the upgrade goes to, by which the pins are read.</param>
/// <param name="PathsOf">
/// The paths each rule of the files answers to, the spent ones among them - read by git, in the files' own syntax, so
/// the review asks about exactly the paths a rule names instead of guessing from the pattern.
/// </param>
internal sealed record IgnoreDecision(IReadOnlySet<string> Held, IReadOnlyList<IgnoreRule> Spent, string Version,
    IReadOnlyDictionary<IgnoreRule, IReadOnlySet<string>> PathsOf)
{
    public static readonly IgnoreDecision None = new(new HashSet<string>(), [], "", new Dictionary<IgnoreRule, IReadOnlySet<string>>());
}

/// <summary>
/// Reads every <c>.dotskitignore</c> of a solution, hands the rules to git, and keeps the candidates git matches. The
/// match is git's own (<see cref="IIgnorePatterns"/>): the rules are read by the syntax the file is written in, a deeper
/// file overriding a higher one, so the tool and the team read the files the same way.
/// </summary>
/// <param name="patterns">The matcher the rules go to.</param>
internal sealed class IgnoreRules(IIgnorePatterns patterns) : IIgnoreRules
{
    public async Task<IgnoreDecision> DecideAsync(string root, string to, string from, IReadOnlyList<string> candidates, CancellationToken ct)
    {
        var files = DotskitIgnores.Load(root);
        if (files.Count == 0)
        {
            return IgnoreDecision.None with { Version = to };
        }

        var rules = files.SelectMany(f => f.Rules).ToList();
        // A pin holds until the upgrade reaches it: at the pin's precision or above, the file merges and the review
        // names the spent rule. A rule with no pin holds always, until a person removes it.
        var spent = rules.Where(r => r.Pin is { } pin && !pin.Holds(to, from)).ToList();
        // Every rule of every file goes to git at once, the spent ones too: a spent rule is still a rule, and its paths
        // are what the review asks about. git names, for each candidate, the rule that decided it - the file's own
        // semantics: a deeper file over a higher one, a later line over an earlier one, ! cancelling.
        var matches = await patterns.MatchAsync(root, files, candidates, ct);
        // git names a rule by its file and its line, the line a comment or a blank shifted along with the rules: the
        // line of the file the team edits, so a rule is found by the two alone.
        var byPlace = rules.ToDictionary(r => (r.Source, r.Line));
        Dictionary<IgnoreRule, IReadOnlySet<string>> pathsOf = matches
            .Where(m => byPlace.ContainsKey((m.Source, m.Line)))
            .GroupBy(m => byPlace[(m.Source, m.Line)])
            .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(m => m.Path).ToHashSet(StringComparer.Ordinal));
        // A path is held when the rule that decided it still holds and the rule is no cancellation: git names a ! rule
        // for the path it brings back, and a path brought back is the template's to write. The last rule wins, as in the
        // files themselves.
        var held = matches
            .Where(m => !m.Pattern.StartsWith('!')
                && byPlace.TryGetValue((m.Source, m.Line), out var rule) && (rule.Pin is not { } pin || pin.Holds(to, from)))
            .Select(m => m.Path).ToHashSet(StringComparer.Ordinal);
        return new IgnoreDecision(held, spent, to, pathsOf);
    }
}

/// <summary>
/// The candidates the set of <c>.dotskitignore</c> files answers to. git itself does the matching, so the semantics are
/// the files' own: the subtree of a folder, a deeper file over a higher one, a later line over an earlier one, and
/// <c>!</c> cancelling an ignore.
/// </summary>
internal interface IIgnorePatterns
{
    /// <summary>The candidates a rule matches, with the rule that decided each, relative to the root with forward slashes.</summary>
    Task<IReadOnlyList<IgnoreMatch>> MatchAsync(string root, IReadOnlyList<DotskitIgnore> files, IReadOnlyList<string> candidates, CancellationToken ct);
}
