using DotsKit.Io;

namespace DotsKit.Merging;

/// <summary>The files the template moved between two of its outputs, by git's rename detection.</summary>
internal interface IRenames
{
    /// <summary>Each moved file, its new path to its old one, relative to the trees.</summary>
    Task<IReadOnlyDictionary<string, string>> FindAsync(string baseDir, string newDir, CancellationToken ct);
}

/// <summary>
/// <c>git diff --no-index -M</c> between the two trees: a file removed from one path and added at another with
/// mostly the same text is a move, so the team's changes to it follow it to the new path.
/// </summary>
internal sealed class GitRenames(IProcessRunner runner) : IRenames
{
    private const char Separator = '\0';
    private const char RenameStatus = 'R';

    /// <summary>git diff exits with 1 when the trees differ, which is the expected case.</summary>
    private const int Differ = 1;

    public async Task<IReadOnlyDictionary<string, string>> FindAsync(string baseDir, string newDir, CancellationToken ct)
    {
        var result = await runner.RunAsync("git", ["diff", "--no-index", "-M", "--name-status", "-z", baseDir, newDir], Path.GetTempPath(), ct);
        if (result.Code is not (0 or Differ))
        {
            throw new InvalidOperationException($"git could not compare the template's two outputs: {result.Error.Trim()}");
        }

        return WithinServices(Parse(result.Output, baseDir, newDir), path => File.Exists(Path.Combine(newDir, path)));
    }

    /// <summary>
    /// The template moves a file inside a service, never from one service to another: where two services have
    /// the same file and both move it, git may pair one service's old path with the other's new one. Such a pair
    /// is taken to the same move inside the old path's own service, or dropped when that service has no such file.
    /// </summary>
    /// <param name="renames">The moves git found between the two trees, new path to old.</param>
    /// <param name="existsInNew">Whether a path, relative to the trees, is a file of the template's new output.</param>
    public static IReadOnlyDictionary<string, string> WithinServices(IReadOnlyDictionary<string, string> renames, Func<string, bool> existsInNew)
    {
        var kept = renames.Where(r => !AcrossServices(r.Key, r.Value)).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        foreach (var (moved, old) in renames.Where(r => AcrossServices(r.Key, r.Value)))
        {
            // The other service's name is in its project folders too: Acme.Shop.Billing/Acme.Shop.Billing.Tests.
            var (to, from) = (Layout.ServiceFolderOf(moved)!, Layout.ServiceFolderOf(old)!);
            var own = from + moved[to.Length..].Replace(NameOf(to), NameOf(from), StringComparison.Ordinal);
            if (existsInNew(own) && !kept.ContainsKey(own))
            {
                kept[own] = old;
            }
        }
        return kept;
    }

    private static bool AcrossServices(string moved, string old) =>
        Layout.ServiceFolderOf(moved) is { } to && Layout.ServiceFolderOf(old) is { } from && to != from;

    private static string NameOf(string serviceFolder) => serviceFolder[(serviceFolder.LastIndexOf('/') + 1)..];

    /// <summary>The renames of git's -z output: a status, then two paths for a rename, one for anything else.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string output, string baseDir, string newDir)
    {
        var fields = output.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Length; i++)
        {
            if (fields[i][0] != RenameStatus)
            {
                i++;
                continue;
            }
            renames[Relative(fields[i + 2], newDir)] = Relative(fields[i + 1], baseDir);
            i += 2;
        }
        return renames;
    }

    private static string Relative(string path, string root)
    {
        var normal = path.Replace('\\', '/');
        var prefix = root.Replace('\\', '/').TrimEnd('/') + "/";
        var at = normal.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? normal : normal[(at + prefix.Length)..];
    }
}
