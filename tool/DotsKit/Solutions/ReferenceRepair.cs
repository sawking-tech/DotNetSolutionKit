using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.Solutions;

/// <summary>Writes the repairs: a reference through <c>dotnet add reference</c>, a using into the file's text.</summary>
internal interface IRepairWriter
{
    /// <summary>Applies the repairs and returns how many changed a file: a using or a reference there already is no change.</summary>
    Task<int> ApplyAsync(IReadOnlyList<Repair> repairs, CancellationToken ct);
}

internal sealed class RepairWriter(IProcessRunner runner) : IRepairWriter
{
    public async Task<int> ApplyAsync(IReadOnlyList<Repair> repairs, CancellationToken ct)
    {
        var changed = 0;
        foreach (var repair in repairs)
        {
            var did = repair switch
            {
                AddReference r => await ReferenceAsync(r, ct),
                AddUsing u => await EditAsync(u.File, text => UsingLines.Add(text, u.Namespace), ct),
                ReplaceUsing u => await EditAsync(u.File, text => UsingLines.Replace(text, u.Namespace, u.With), ct),
                _ => false,
            };
            changed += did ? 1 : 0;
        }
        return changed;
    }

    /// <summary>A project the team removed, on either end, gets no reference.</summary>
    private async Task<bool> ReferenceAsync(AddReference r, CancellationToken ct)
    {
        if (!File.Exists(r.Project) || !File.Exists(r.Referenced))
            return false;
        var before = await File.ReadAllTextAsync(r.Project, ct);
        await runner.RunCheckedAsync("dotnet", ["add", r.Project, "reference", r.Referenced], Path.GetDirectoryName(r.Project)!, ct);
        return await File.ReadAllTextAsync(r.Project, ct) != before;
    }

    private static async Task<bool> EditAsync(string file, Func<string, string> edit, CancellationToken ct)
    {
        var text = FileText.Read(Path.GetDirectoryName(file), Path.GetFileName(file));
        if (text is null || text.IsBinary)
            return false;
        var edited = edit(text.Text);
        if (edited == text.Text)
            return false;
        await File.WriteAllBytesAsync(file, (text with { Text = edited }).ToBytes(), ct);
        return true;
    }
}

/// <summary>What the repair did: the last build, the files' changes it made, and why it stopped when a write failed.</summary>
internal sealed record RepairResult(ProcessResult Build, int Changed, string? Stopped);

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

    public async Task<RepairResult> RunAsync(string root, TypeMap map, ProcessResult build, CancellationToken ct)
    {
        var made = new HashSet<Repair>();
        var changed = 0;
        for (var round = 0; round < MaxRounds && build.Code != 0; round++)
        {
            var repairs = ReferenceRepairs.Decide(BuildErrors.Parse(build.Output), map, root, UsingsOf).Where(r => !made.Contains(r)).ToList();
            if (repairs.Count == 0)
                break;
            try
            {
                changed += await writer.ApplyAsync(repairs, ct);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return new(build, changed, e.Message.Split('\n')[0].Trim());
            }
            made.UnionWith(repairs);
            build = await builder.BuildAsync(root, ct);
        }
        return new(build, changed, null);
    }

    private static IReadOnlyList<string> UsingsOf(string file) => File.Exists(file) ? UsingLines.Of(File.ReadAllText(file)) : [];
}
