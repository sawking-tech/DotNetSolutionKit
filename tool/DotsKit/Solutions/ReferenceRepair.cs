using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.Solutions;

/// <summary>Writes the repairs: a reference through <c>dotnet add reference</c>, a using into the file's text.</summary>
internal interface IRepairWriter
{
    /// <summary>Applies the repairs and returns those that changed a file: a using or a reference there already is no change.</summary>
    Task<IReadOnlyList<Repair>> ApplyAsync(IReadOnlyList<Repair> repairs, CancellationToken ct);
}

internal sealed class RepairWriter(IProcessRunner runner) : IRepairWriter
{
    public async Task<IReadOnlyList<Repair>> ApplyAsync(IReadOnlyList<Repair> repairs, CancellationToken ct)
    {
        var applied = new List<Repair>();
        foreach (var repair in repairs)
        {
            var did = repair switch
            {
                AddReference r => await ReferenceAsync(r, ct),
                AddUsing u => await EditAsync(u.File, text => UsingLines.Add(text, u.Namespace), ct),
                ReplaceUsing u => await EditAsync(u.File, text => UsingLines.Replace(text, u.Namespace, u.With), ct),
                _ => false,
            };
            if (did)
            {
                applied.Add(repair);
            }
        }
        return applied;
    }

    /// <summary>A project the team removed, on either end, gets no reference.</summary>
    private async Task<bool> ReferenceAsync(AddReference r, CancellationToken ct)
    {
        if (!File.Exists(r.Project) || !File.Exists(r.Referenced))
        {
            return false;
        }

        var before = await File.ReadAllTextAsync(r.Project, ct);
        await runner.RunCheckedAsync("dotnet", ["add", r.Project, "reference", r.Referenced], Path.GetDirectoryName(r.Project)!, ct);
        return await File.ReadAllTextAsync(r.Project, ct) != before;
    }

    private static async Task<bool> EditAsync(string file, Func<string, string> edit, CancellationToken ct)
    {
        var text = FileText.Read(Path.GetDirectoryName(file), Path.GetFileName(file));
        if (text is null || text.IsBinary)
        {
            return false;
        }

        var edited = edit(text.Text);
        if (edited == text.Text)
        {
            return false;
        }

        await File.WriteAllBytesAsync(file, (text with { Text = edited }).ToBytes(), ct);
        return true;
    }
}

/// <summary>What the repair did: the last build, the repairs that changed a file, and why it stopped when a write failed.</summary>
internal sealed record RepairResult(ProcessResult Build, IReadOnlyList<Repair> Applied, string? Stopped);

/// <summary>
/// After a build that fails, repairs the references and usings of the types the template moved and builds again,
/// round after round, while each round finds a repair it has not made: a project that builds lets the projects
/// that use it show their own errors in the next round (#117). It runs after the plan is written, so a write that
/// fails stops the repair and is reported, and what is written stays for git to show.
/// </summary>
internal sealed class ReferenceRepair(IRepairWriter writer, IBuilder builder)
{
    /// <summary>A round per layer of projects that reference each other is plenty; a solution has few.</summary>
    private const int MaxRounds = 10;

    /// <param name="root">The solution's root.</param>
    /// <param name="map">The types the template moved.</param>
    /// <param name="build">The build that failed.</param>
    /// <param name="ct">Cancels the repair.</param>
    /// <param name="waiting">Files, relative to the root, the repair leaves alone: the template's files of the parts not merged yet.</param>
    public async Task<RepairResult> RunAsync(string root, TypeMap map, ProcessResult build, CancellationToken ct, IReadOnlySet<string>? waiting = null)
    {
        var made = new HashSet<Repair>();
        var applied = new List<Repair>();
        for (var round = 0; round < MaxRounds && build.Code != 0; round++)
        {
            var repairs = ReferenceRepairs.Decide(BuildErrors.Parse(build.Output), map, root, UsingsOf)
                .Where(r => !made.Contains(r) && waiting?.Contains(Path.GetRelativePath(root, RepairReport.FileOf(r)).Replace('\\', '/')) != true).ToList();
            if (repairs.Count == 0)
            {
                break;
            }

            try
            {
                applied.AddRange(await writer.ApplyAsync(repairs, ct));
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return new(build, applied, e.Message.Split('\n')[0].Trim());
            }
            made.UnionWith(repairs);
            build = await builder.BuildAsync(root, ct);
        }
        return new(build, applied, null);
    }

    private static IReadOnlyList<string> UsingsOf(string file) => File.Exists(file) ? UsingLines.Of(File.ReadAllText(file)) : [];

    /// <summary>
    /// Before any build: every using of the solution's code that names a namespace the template gave up is replaced
    /// by where its types went, and the project gets their projects, from the text alone. A build then has less to
    /// find, often nothing. A file left in conflict, or of a project left in conflict, waits for its resolution and
    /// the build. Returns the repairs that changed a file.
    /// </summary>
    /// <param name="root">The solution's root.</param>
    /// <param name="map">The types the template moved between the solution's version and the next.</param>
    /// <param name="conflicts">The files left in conflict, relative to the root.</param>
    /// <param name="ct">Cancels the repair; what is written stays for git to show.</param>
    public async Task<IReadOnlyList<Repair>> BeforeBuildAsync(string root, TypeMap map, IReadOnlyList<string> conflicts, CancellationToken ct)
    {
        var left = conflicts.Select(c => Path.GetFullPath(Path.Combine(root, c))).ToHashSet(PathComparer);
        var repairs = ReferenceRepairs.FromText(SourceFiles(root).Where(f => !left.Contains(f.File) && !left.Contains(f.Project)), map, root);
        return repairs.Count == 0 ? [] : await writer.ApplyAsync(repairs, ct);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// The C# files under src with their project, the nearest .csproj above them, their usings and the namespaces
    /// they declare. A folder with two projects names no one project, and its files are left to the build.
    /// </summary>
    private static IEnumerable<SourceFile> SourceFiles(string root)
    {
        var src = Path.Combine(root, Layout.SourceFolder);
        if (!Directory.Exists(src))
        {
            yield break;
        }

        var projects = Directory.GetFiles(src, "*" + Layout.ProjectExtension, SearchOption.AllDirectories)
            .GroupBy(p => Path.GetDirectoryName(p)!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => Path.GetFullPath(g.Single()), StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetRelativePath(src, file).Replace('\\', '/').Split('/').Any(Layout.Ignored.Contains) || ProjectOf(file, projects) is not { } project)
            {
                continue;
            }

            var text = File.ReadAllText(file);
            yield return new SourceFile(Path.GetFullPath(file), project, UsingLines.Of(text), UsingLines.Declared(text));
        }
    }

    private static string? ProjectOf(string file, Dictionary<string, string> projects)
    {
        for (var dir = Path.GetDirectoryName(file); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (projects.TryGetValue(dir, out var project))
            {
                return project;
            }
        }

        return null;
    }
}
