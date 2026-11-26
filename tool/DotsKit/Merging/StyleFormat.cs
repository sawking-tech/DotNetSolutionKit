using DotsKit.Io;

namespace DotsKit.Merging;

/// <summary>
/// Lays out the template's side of a merge by the team's own style: the files both sides changed are formatted in a
/// copy of the template's output, by the template's rules united with the solution's root <c>.editorconfig</c>
/// (<see cref="StyleUnion"/>). The union and the copy live in folders of the run's own; the solution's files, its
/// <c>.editorconfig</c> among them, are never written.
///
/// Only the template's side is formatted. The solution's side is the team's code, laid out by the team, and formatting
/// it would be the tool's opinion of its taste; laid out by the team's rules, the two sides meet with no style
/// difference left for the merge to report as a conflict.
///
/// The layout alone is applied - the indentation, the spacing, the blank lines - and never the analyzers: a fix of an
/// analyzer rewrites code, and a rewrite is a change neither side made. The text of the code stays what the template
/// wrote; only its laying out changes.
/// </summary>
internal interface IStyleFormat
{
    /// <summary>
    /// The template's tree at <paramref name="nextDir"/> laid out by the team's style, or <paramref name="nextDir"/>
    /// itself when nothing is both-changed, the solution has no <c>.editorconfig</c>, or the formatter cannot run.
    /// </summary>
    /// <param name="root">The solution's root, where the team's <c>.editorconfig</c> is read from.</param>
    /// <param name="nextDir">The template's output, as generated.</param>
    /// <param name="paths">The files both sides changed, as the two diffs against the base name them.</param>
    /// <param name="ct">Cancels the formatting.</param>
    Task<string> TemplateSideAsync(string root, string nextDir, IReadOnlyCollection<string> paths, CancellationToken ct);
}

/// <param name="runner">Runs dotnet format.</param>
/// <param name="style">The template's own style, as the package carries it.</param>
internal sealed class DotnetFormat(IProcessRunner runner, Func<string> style) : IStyleFormat
{
    /// <summary>The team's file, read by the editorconfig rule from the folder of the file it formats.</summary>
    private const string TeamFile = ".editorconfig";

    /// <summary>The layout alone, which no analyzer fix touches: the code keeps the text the template gave it.</summary>
    private const string Layout = "whitespace";

    public async Task<string> TemplateSideAsync(string root, string nextDir, IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        var team = Path.Combine(root, TeamFile);
        var include = string.Join(' ', paths
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => FileText.Read(nextDir, p) is not null)
            .Select(p => p.Replace('\\', '/'))
            .Order(StringComparer.Ordinal));
        if (include.Length == 0 || !File.Exists(team))
        {
            return nextDir;
        }

        // A copy, not the output itself: the report of the plan reads the tree the template generated, and the copy is
        // what the merge takes for the template's side.
        var styled = Folders.NewTemp("styled");
        Folders.Copy(nextDir, styled);
        // The union sits in the root of the copy, where the team's file sits in the solution: the two files' rules in
        // one, the team's last, so its rule wins where both name one.
        await File.WriteAllTextAsync(Path.Combine(styled, StyleUnion.FileName), StyleUnion.Of(style(), await File.ReadAllTextAsync(team, ct)), ct);
        // The folder form: no project and no build, the files it is given and the editorconfig above them.
        var format = await runner.RunAsync("dotnet", ["format", Layout, "--folder", "--include", include, "--verbosity", "quiet"], styled, ct);
        // A formatter that cannot run leaves the merge as it was before this feature: the template's side by its bytes.
        // The build after the merge is the check of the result, and the tool does not fail an upgrade over a formatter.
        return format.Code == 0 ? styled : nextDir;
    }
}
