using System.Security.Cryptography;
using System.Text;
using DotsKit.Io;

namespace DotsKit.Commands;

/// <summary>
/// The solution's tree as a command found it, so the plan built from it is written only over the same tree: the
/// commit of a clean tree, or, under --allow-dirty, its files as they were - each path, size and time of the last
/// write - since their changes are in no commit to come back from.
/// </summary>
/// <param name="Head">The commit of a clean tree; null under --allow-dirty or with no solution yet.</param>
/// <param name="Files">The stamp of the files under --allow-dirty; null otherwise.</param>
internal sealed record TreeStart(string? Head, string? Files)
{
    /// <summary>No tree to check: a new solution is made in an empty folder.</summary>
    public static TreeStart None { get; } = new(null, null);

    public static async Task<TreeStart> OfAsync(IGit git, string root, Options options, CancellationToken ct) =>
        options.AllowDirty ? new(null, StampOf(root)) : new(await git.CleanHeadAsync(root, ct), null);

    /// <summary>The tree is as it was found: at the same commit and still clean, or with the same files.</summary>
    public async Task<bool> IsStillAsync(IGit git, string root, CancellationToken ct) =>
        (Head is null || await git.CleanHeadAsync(root, ct) == Head) && (Files is null || StampOf(root) == Files);

    private static string StampOf(string root)
    {
        var lines = Folders.RelativeFiles(root).Order(StringComparer.Ordinal)
            .Select(path => new FileInfo(Path.Combine(root, path)) is var file ? $"{path}\0{file.Length}\0{file.LastWriteTimeUtc.Ticks}\n" : "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines))));
    }
}
