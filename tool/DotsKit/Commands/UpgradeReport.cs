using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Commands;

/// <summary>
/// A page about an upgrade, written beside the manifest: what the template gave, what git left in conflict, what the
/// tool changed by itself, and what the build still names. It is a record to come back to, not a step of the upgrade:
/// a failure to write it never fails the run.
/// </summary>
internal static class UpgradeReport
{
    /// <summary>Where the pages live, under the manifest's folder; the template's gitignore keeps them out of git.</summary>
    public const string Folder = ".dotskit/reports";

    /// <summary>Writes the page and returns where it went; null when there is nothing to report.</summary>
    /// <param name="plan">The plan the run was made from.</param>
    /// <param name="written">What the write left: the conflicts, the repairs, the files --force took.</param>
    /// <param name="build">The output of the build after the write, or null when it was skipped.</param>
    public static string? Write(Plan plan, WriteResult written, string? build)
    {
        if (plan.ByGit is null && plan.Changes.Count == 0)
        {
            return null;
        }

        var lines = new List<string>
        {
            $"# Upgrade {plan.From ?? "new"} -> {plan.Next.Upgrade?.To ?? plan.Next.Template}",
            string.Empty,
            "Written by dotskit beside the manifest. Not a step of the upgrade; nothing here needs an answer.",
            string.Empty,
        };

        Section(lines, "The template's change", [
            $"added {Count(plan, ChangeKind.Add)}, changed {Count(plan, ChangeKind.Update)}, removed {Count(plan, ChangeKind.Delete)}",
        ]);

        Section(lines, "Left in conflict", written.Conflicts.Count == 0
            ? ["nothing"]
            : [$"by project: {ByService(written.Conflicts, ProjectFolders(plan.Root))}", .. written.Conflicts.Select(p => $"  {p}")]);

        if (written.Taken.Count > 0)
        {
            Section(lines, "Took the template's side (--force)", written.Taken);
        }

        Section(lines, "Changed by the tool alone", written.Repaired.Count == 0 && plan.Formatted == 0
            ? ["nothing"]
            : [
                $"{written.Repaired.Count} using line(s) and reference(s) across {written.Repaired.Select(RepairReport.FileOf).Distinct(StringComparer.Ordinal).Count()} file(s)",
                $"{plan.Formatted} file(s) laid out by the team's style",
                .. RepairReport.ByProject(written.Repaired),
            ]);

        if (plan.Ignore is { Held.Count: > 0 } ignore)
        {
            Section(lines, "Held by .dotskitignore", [.. ignore.Held.Order(StringComparer.Ordinal).Select(p => $"  {p}")]);
        }

        if (build is not null)
        {
            var errors = BuildErrors.ByProject(build).ToList();
            Section(lines, "The build after the write", errors.Count == 0 ? ["the solution builds"] : [.. errors.Select(e => $"  {e}")]);
        }

        var path = Path.Combine(plan.Root, Folder, $"{plan.Next.Template}-{DateTime.Now:yyyy-MM-dd-HHmm}.md");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join('\n', lines) + "\n");
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The conflicts grouped by the project they sit in, most first.</summary>
    private static string ByService(IReadOnlyList<string> conflicts, IReadOnlyList<string> projects) =>
        string.Join(", ", conflicts
            .GroupBy(p => ServiceOf(p, projects), StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} {g.Count()}"));

    /// <summary>
    /// The project a path sits in, by the longest project folder above it: <c>src/common/X/...</c> names X. A path
    /// outside every project folder is named by its own first folder under `src`, or "root".
    /// </summary>
    private static string ServiceOf(string path, IReadOnlyList<string> projects)
    {
        var normalized = path.Replace('\\', '/');
        var owner = projects
            .Where(p => normalized.StartsWith(p + "/", StringComparison.Ordinal))
            .MaxBy(p => p.Length);
        if (owner is not null)
        {
            return owner.Split('/').Last();
        }

        var parts = normalized.Split('/');
        return parts.Length > 1 ? parts[1] : "root";
    }

    /// <summary>
    /// The project folders of a solution, relative and with forward slashes: every folder that holds a `.csproj`.
    /// </summary>
    private static IReadOnlyList<string> ProjectFolders(string root) =>
        [.. Folders.RelativeFiles(root)
            .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetDirectoryName(p)!.Replace(Path.DirectorySeparatorChar, '/'))
            .Distinct(StringComparer.Ordinal)];

    private static int Count(Plan plan, ChangeKind kind) => plan.Changes.Count(c => c.Kind == kind);

    private static void Section(List<string> lines, string title, IEnumerable<string> body)
    {
        lines.Add($"## {title}");
        lines.Add(string.Empty);
        lines.AddRange(body);
        lines.Add(string.Empty);
    }
}
