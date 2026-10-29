using DotsKit.Io;

namespace DotsKit.Merging;

/// <summary>The solution's files its git ignores, such as a local appsettings.Secrets.json: no merge writes them.</summary>
internal interface IIgnoredFiles
{
    /// <summary>Paths relative to <paramref name="root"/>; none when the solution is not in git.</summary>
    Task<IReadOnlySet<string>> InAsync(string root, CancellationToken ct);
}

/// <summary>
/// <c>git ls-files --others --ignored --exclude-standard</c>: the files on disk the solution's rules leave out of git.
/// Every git command here is listed in docs/getting-started/dotskit.md, "What dotskit runs in git": a command added
/// or changed here goes there too.
/// </summary>
internal sealed class GitIgnoredFiles(IProcessRunner runner) : IIgnoredFiles
{
    public async Task<IReadOnlySet<string>> InAsync(string root, CancellationToken ct)
    {
        var listed = await runner.RunAsync("git", ["ls-files", "-z", "--others", "--ignored", "--exclude-standard"], root, ct);
        // A path of the template's output may differ in case from the file on disk, as Windows lets it.
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (listed.Code == 0)
        {
            files.UnionWith(listed.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }

        return files;
    }
}
