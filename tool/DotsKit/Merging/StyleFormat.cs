using DotsKit.Io;

namespace DotsKit.Merging;

/// <summary>
/// Lays out a tree of the template's output by the team's style, in a copy of its own; the solution's files are never
/// written. Two passes, both needed: <c>whitespace</c> for the indent and the brace, <c>style</c> at
/// <c>--severity info</c> for the usings, the namespace and the blank lines.
/// </summary>
internal interface IStyleFormat
{
    /// <summary>
    /// The tree at <paramref name="tree"/> laid out by the team's style, or <paramref name="tree"/> itself when nothing
    /// is both-changed, the solution has no <c>.editorconfig</c>, or the formatter cannot run.
    /// </summary>
    /// <param name="root">The solution's root, where the team's <c>.editorconfig</c> is read from.</param>
    /// <param name="tree">A tree of the template's output.</param>
    /// <param name="paths">The files both sides changed, as the two diffs against the base name them.</param>
    /// <param name="ct">Cancels the formatting.</param>
    Task<string> LaidOutAsync(string root, string tree, IReadOnlyCollection<string> paths, CancellationToken ct);

    /// <summary>The files the last <see cref="LaidOutAsync"/> laid out, for the report of the upgrade.</summary>
    int Formatted { get; }
}

/// <param name="runner">Runs dotnet format.</param>
/// <param name="style">The template's own style, as the repository holds it.</param>
internal sealed class DotnetFormat(IProcessRunner runner, Func<string> style) : IStyleFormat
{
    /// <summary>The team's file, read by the editorconfig rule from the folder of the file it formats.</summary>
    private const string TeamFile = ".editorconfig";

    /// <summary>The layout alone: the indent, the brace, the spacing.</summary>
    private const string Layout = "whitespace";

    /// <summary>The code rules: the usings, the namespace, the blank lines, and a rewrite where a rule is one.</summary>
    private const string Style = "style";

    /// <summary>The threshold a rule at <c>suggestion</c> is applied at, the severity the template's own file states.</summary>
    private const string Info = "info";

    /// <summary>The union's name in the copy: not the team's file name, so a copy that carries one keeps it.</summary>
    private const string UnionName = "dotskit.editorconfig";

    public int Formatted { get; private set; }

    public async Task<string> LaidOutAsync(string root, string tree, IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        var team = Path.Combine(root, TeamFile);
        // Separate arguments: one holding several paths matches no file, and the formatter then exits zero having
        // changed nothing.
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

        // A copy: the plan reads the tree the template generated, and this copy is what the merge takes.
        var styled = Folders.NewTemp("styled");
        Folders.Copy(tree, styled);
        var wroteSolution = !Folders.RelativeFiles(tree).Any(IsSolution);
        // Beside the output's own .editorconfig, never over it: a file the merge expects must not vanish.
        var union = Path.Combine(styled, UnionName);
        await File.WriteAllTextAsync(union, StyleUnion.Of(style(), await File.ReadAllTextAsync(team, ct)), ct);
        // The files read through the projects that compile them; the formatter restores them, or it changes nothing.
        var solution = await SolutionOfAsync(styled, include, ct);
        if (solution is null)
        {
            File.Delete(union);
            return tree;
        }

        // Two passes, and both are needed: whitespace applies the indent and the brace and leaves the usings, the
        // namespace and the blank lines; style applies those and not the indent. --severity info is the threshold at
        // which a rule at suggestion applies, which is what the template's file states.
        var format = await runner.RunAsync("dotnet",
            ["format", solution, Layout, "--include", .. include, "--verbosity", "quiet"], styled, ct);
        format = format.Code == 0
            ? await runner.RunAsync("dotnet",
                ["format", solution, Style, "--include", .. include, "--severity", Info, "--verbosity", "quiet"], styled, ct)
            : format;
        // Both are the tool's: left in, the merge would report them as files the template added.
        File.Delete(union);
        if (wroteSolution)
        {
            File.Delete(Path.Combine(styled, solution));
        }
        // A formatter that cannot run leaves the side by its bytes; the build after the merge is the real check.
        Formatted = format.Code == 0 ? Count(include, tree, styled) : 0;
        return format.Code == 0 ? styled : tree;
    }

    /// <summary>The files whose bytes the formatter changed.</summary>
    private static int Count(IReadOnlyList<string> include, string before, string after) =>
        include.Count(p => FileText.Read(before, p) is { } was && FileText.Read(after, p) is { } now && !now.SameTextAs(was));

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
