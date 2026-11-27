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
/// The pass is <c>dotnet format whitespace</c>, and never <c>style</c>. Both read the union, but whitespace only lays
/// lines out - the brace of a new line, the indent, the spacing - while style applies analyzer fixes, and a fix
/// rewrites the code: a file-scoped namespace becomes a block, an <c>if</c> becomes a conditional expression. A merge
/// keeps what the code says; the cost is stated in <see cref="StyleUnion"/> - the order of the usings and the
/// namespace declaration stay as the template wrote them.
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

    /// <summary>The bytes and the layout: the brace, the indent, the spacing. It changes no code.</summary>
    private const string Layout = "whitespace";

    /// <summary>The union's name in the copy: not the team's file name, so a copy that carries one keeps it.</summary>
    private const string UnionName = "dotskit.editorconfig";

    public async Task<string> LaidOutAsync(string root, string tree, IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        var team = Path.Combine(root, TeamFile);
        // The paths go to dotnet format as separate arguments: --include takes the paths that follow it, and one
        // argument holding several of them matches no file - the formatter reports nothing changed and exits zero,
        // which is the worst way for a step to fail.
        var include = paths
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(p => FileText.Read(tree, p) is not null)
            .Select(p => p.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (include.Count == 0 || !File.Exists(team))
        {
            return tree;
        }

        // A copy, not the output itself: the report of the plan reads the tree the template generated, and the copy is
        // what the merge takes for the template's side.
        var styled = Folders.NewTemp("styled");
        Folders.Copy(tree, styled);
        var wroteSolution = !Folders.RelativeFiles(tree).Any(IsSolution);
        // The template's own .editorconfig, when the output carries one, is the team's file in a solution and stays: the
        // union is written beside it under the tool's name, so the copy never loses a file the merge expects.
        var union = Path.Combine(styled, UnionName);
        await File.WriteAllTextAsync(union, StyleUnion.Of(style(), await File.ReadAllTextAsync(team, ct)), ct);
        // The solution form, as the tool already builds one for the repairs: the files given, read through the projects
        // that compile them. The restore is the formatter's own: without it the projects load with no references and it
        // changes nothing, silently and with a zero exit code.
        var solution = await SolutionOfAsync(styled, include, ct);
        if (solution is null)
        {
            File.Delete(union);
            return tree;
        }

        // One pass, whitespace, and never style: whitespace reads the options of the union - the brace, the indent, the
        // spacing - and changes nothing but the layout. `dotnet format style` applies analyzer fixes, and a fix rewrites
        // the code; the order of the usings and the namespace declaration are style rules, so the merge leaves those as
        // the template wrote them.
        var format = await runner.RunAsync("dotnet",
            ["format", solution, Layout, "--include", .. include, "--verbosity", "quiet"], styled, ct);
        // The union and the solution this run wrote are the tool's, not the template's: they go before the tree becomes
        // the template's side, or the merge reports them as files the template added and conflicts on the team's own.
        File.Delete(union);
        if (wroteSolution)
        {
            File.Delete(Path.Combine(styled, solution));
        }
        // A formatter that cannot run leaves the merge as it was before this feature: the template's side by its bytes.
        // The build after the merge is the check of the result, and the tool does not fail an upgrade over a formatter.
        return format.Code == 0 ? styled : tree;
    }

    /// <summary>The solution file of a tree, its own or one written for the projects that hold the files; null when there is none.</summary>
    private async Task<string?> SolutionOfAsync(string tree, IReadOnlyList<string> include, CancellationToken ct)
    {
        // The tree of a solution that already carries one has it; an output of the template carries none.
        if (Folders.RelativeFiles(tree).FirstOrDefault(IsSolution) is { } own)
        {
            return own;
        }

        HashSet<string> files = new(include, StringComparer.Ordinal);
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
