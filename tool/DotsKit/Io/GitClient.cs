namespace DotsKit.Io;

/// <summary>
/// The calls of git that take a list of paths. A list of paths goes to git in
/// a file (<c>--pathspec-from-file</c>), never on the command line, so a solution of any size fits
/// (<see cref="CommandLine"/>).
/// Every git command here is listed in docs/getting-started/dotskit.md, "What dotskit runs in git": a command added
/// or changed here goes there too.
/// </summary>
internal sealed class GitClient(IProcessRunner runner)
{
    private const string PathspecFromFile = "--pathspec-from-file";
    private const string PathspecFileNul = "--pathspec-file-nul";

    public async Task StageAsync(string root, IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        if (paths.Count > 0)
        {
            await runner.RunCheckedAsync("git", await WithPathsAsync(["add"], paths, ct), root, ct);
        }
    }

    /// <summary>
    /// Takes the paths out of an index of its own (<paramref name="environment"/>), whatever the files on disk are:
    /// -f, as git would otherwise compare that index with the solution's files.
    /// </summary>
    public async Task RemoveCachedAsync(string root, IReadOnlyCollection<string> paths, IReadOnlyDictionary<string, string> environment, CancellationToken ct)
    {
        if (paths.Count > 0)
        {
            await runner.RunCheckedAsync("git", await WithPathsAsync(["rm", "--cached", "-q", "-f"], paths, ct), root, ct, environment);
        }
    }

    /// <summary>The command, with the paths written to a file of its own and named by it.</summary>
    private static async Task<string[]> WithPathsAsync(string[] command, IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        var list = Path.Combine(Folders.NewTemp("pathspec"), "paths");
        await File.WriteAllTextAsync(list, string.Join('\0', paths), ct);
        return [.. command, PathspecFromFile, list, PathspecFileNul];
    }
}
