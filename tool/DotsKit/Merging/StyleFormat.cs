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
/// The rules are the ones <c>dotnet format style</c> applies - the braces, the usings, the namespace, the blank lines -
/// and not only the layout of <c>whitespace</c>: a team that writes braces differently sees exactly those lines
/// conflict, and a merge that leaves them out has done nothing for the case it exists for. A fix of a rule that
/// rewrites code rather than laying it out is the team's to make: the template's rules name none, and the ones they
/// do name are the layout. The file is formatted by a rule at <c>warning</c> and up, which is how the template's own
/// <c>.editorconfig</c> holds them.
/// </summary>
internal interface IStyleFormat
{
    /// <summary>
    /// The tree at <paramref name="tree"/> laid out by the team's style, or <paramref name="tree"/> itself when nothing
    /// is both-changed, the solution has no <c>.editorconfig</c>, or the formatter cannot run. Both sides of the merge
    /// go through it - the output the solution came from and the one it goes to - so git reads one style against one.
    /// </summary>
    /// <param name="root">The solution's root, where the team's <c>.editorconfig</c> is read from.</param>
    /// <param name="tree">A tree of the template's output.</param>
    /// <param name="paths">The files both sides changed, as the two diffs against the base name them.</param>
    /// <param name="ct">Cancels the formatting.</param>
    Task<string> LaidOutAsync(string root, string tree, IReadOnlyCollection<string> paths, CancellationToken ct);
}

/// <param name="runner">Runs dotnet format.</param>
/// <param name="style">The template's own style, at the severities a formatter applies.</param>
internal sealed class DotnetFormat(IProcessRunner runner, Func<string> style) : IStyleFormat
{
    /// <summary>The team's file, read by the editorconfig rule from the folder of the file it formats.</summary>
    private const string TeamFile = ".editorconfig";

    /// <summary>The style rules: the braces, the usings, the namespace, the blank lines.</summary>
    private const string Style = "style";

    /// <summary>The bytes: the indent, the spacing, the end of a line. The style pass leaves these to it.</summary>
    private const string Layout = "whitespace";


    public async Task<string> LaidOutAsync(string root, string tree, IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        var team = Path.Combine(root, TeamFile);
        var include = string.Join(' ', paths
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => FileText.Read(tree, p) is not null)
            .Select(p => p.Replace('\\', '/'))
            .Order(StringComparer.Ordinal));
        if (include.Length == 0 || !File.Exists(team))
        {
            return tree;
        }

        // A copy, not the output itself: the report of the plan reads the tree the template generated, and the copy is
        // what the merge takes for the template's side.
        var styled = Folders.NewTemp("styled");
        Folders.Copy(tree, styled);
        var wroteSolution = !Folders.RelativeFiles(tree).Any(IsSolution);
        // The union sits in the root of the copy, where the team's file sits in the solution: the two files' rules in
        // one, the team's last, so its rule wins where both name one.
        await File.WriteAllTextAsync(Path.Combine(styled, StyleUnion.FileName), StyleUnion.Of(style(), await File.ReadAllTextAsync(team, ct)), ct);
        // The solution form, as the tool already builds one for the repairs: the files given, read through the projects
        // that compile them. The restore is the formatter's own: without it the projects load with no references and it
        // changes nothing, silently and with a zero exit code.
        var solution = await SolutionOfAsync(styled, include, ct);
        if (solution is null)
        {
            return tree;
        }

        // The layout first, and it carries most of a team's style: dotnet format applies the brace of a new line, the
        // indent and the spacing of the token in whitespace, not in style. The style pass follows for the rules that are
        // not layout - the order of the usings, the namespace, the blank lines - and does nothing for a rule it cannot
        // fix. Whichever pass fails, the other still runs: a formatter that cannot do its part leaves that part as it
        // was, and the merge falls back to the bytes for what is left.
        var format = await runner.RunAsync("dotnet",
            ["format", solution, Layout, "--include", include, "--verbosity", "quiet"], styled, ct);
        var rules = await runner.RunAsync("dotnet",
            ["format", solution, Style, "--include", include, "--verbosity", "quiet"], styled, ct);
        // The union and the solution this run wrote are the tool's, not the template's: they go before the tree becomes
        // the template's side, or the merge reports them as files the template added and conflicts on the team's own.
        File.Delete(Path.Combine(styled, StyleUnion.FileName));
        if (wroteSolution)
        {
            File.Delete(Path.Combine(styled, solution));
        }
        // A formatter that cannot run leaves the merge as it was before this feature: the template's side by its bytes.
        // The build after the merge is the check of the result, and the tool does not fail an upgrade over a formatter.
        return format.Code == 0 && rules.Code == 0 ? styled : tree;
    }

    /// <summary>The solution file of a tree, its own or one written for the projects that hold the files; null when there is none.</summary>
    private async Task<string?> SolutionOfAsync(string tree, string include, CancellationToken ct)
    {
        // The tree of a solution that already carries one has it; an output of the template carries none.
        if (Folders.RelativeFiles(tree).FirstOrDefault(IsSolution) is { } own)
        {
            return own;
        }

        HashSet<string> files = new(include.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        var projects = Folders.RelativeFiles(tree)
            .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(p => files.Any(f => f.StartsWith(Path.GetDirectoryName(p)!.Replace('\\', '/') + "/", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (projects.Count == 0)
        {
            return null;
        }

        // dotnet takes the next SDK's format - .slnx, not .sln - so the file is found by what it is, not by a name.
        var create = await runner.RunAsync("dotnet", ["new", "sln", "-n", "style", "-o", tree], tree, ct);
        if (create.Code != 0 || Folders.RelativeFiles(tree).FirstOrDefault(IsSolution) is not { } written)
        {
            return null;
        }

        var add = await runner.RunAsync("dotnet", ["sln", written, "add", .. projects], tree, ct);
        return add.Code == 0 ? written : null;
    }

    /// <summary>A solution file, of either form dotnet writes: a classic .sln or the .slnx of a newer SDK.</summary>
    private static bool IsSolution(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
}
