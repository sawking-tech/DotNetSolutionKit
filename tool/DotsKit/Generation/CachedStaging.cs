using System.Security.Cryptography;
using System.Text;
using DotsKit.Io;

namespace DotsKit.Generation;

/// <summary>
/// The template's output for a manifest, generated once and kept in the temp folder: an upgrade a part at a time
/// asks for the same two outputs at every part, and the generation is most of a part's time. Each call gets a copy
/// of its own, since a plan rewrites the trees it is given. An output older than <see cref="Kept"/> is removed. The
/// cache only saves time: an output it cannot keep or read is generated again.
/// </summary>
internal sealed class CachedStaging(IStaging inner, string toolVersion, Func<string?> sourcesFolder) : IStaging
{
    private static readonly string Folder = Path.Combine(Folders.RealPath(Path.GetTempPath()), "dotskit", "outputs");

    /// <summary>How long an output is kept: an upgrade's parts come in one sitting, rarely over days.</summary>
    private static readonly TimeSpan Kept = TimeSpan.FromDays(2);

    private const string Tree = "solution";

    /// <summary>Written last into a kept output: one without it is half written, or being removed, and is not read.</summary>
    private const string Complete = "complete";

    private const string Removed = ".removed-";

    private const int KeyLength = 24;

    public async Task<string> BuildAsync(Manifest manifest, bool withSolutionFile, CancellationToken ct)
    {
        RemoveOld();
        var cached = Path.Combine(Folder, Key(manifest, withSolutionFile, toolVersion + Build, SourcesStamp(manifest)));
        if (TryCopy(cached) is { } copy)
        {
            return copy;
        }

        var built = await inner.BuildAsync(manifest, withSolutionFile, ct);
        Keep(built, cached);
        return TryCopy(cached) ?? CopyOf(built);
    }

    /// <summary>
    /// What tells two outputs apart: the tool, the manifest without the upgrade in progress - its parts do not change
    /// what the template gives - whether All.sln is made, and for the template's sources, the state of their files.
    /// </summary>
    public static string Key(Manifest manifest, bool withSolutionFile, string toolVersion, string sourcesStamp)
    {
        var text = string.Join('\n', toolVersion, (manifest with { Upgrade = null }).Serialize(), withSolutionFile, sourcesStamp);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..KeyLength];
    }

    /// <summary>The build of the tool itself: a tool rebuilt under the same version generates afresh.</summary>
    private static string Build => typeof(CachedStaging).Module.ModuleVersionId.ToString("N");

    /// <summary>
    /// The template's sources change under a developer: every file's path, size and last write stand for them, so a
    /// rename, a removal or an older file copied in changes the stamp.
    /// </summary>
    private string SourcesStamp(Manifest manifest)
    {
        if (manifest.Template != TemplateSource.SourcesVersion || sourcesFolder() is not { } folder || !Directory.Exists(folder))
        {
            return "";
        }

        var files = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(f => $"{Path.GetRelativePath(folder, f.FullName)}|{f.Length}|{f.LastWriteTimeUtc.Ticks}")
            .Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', files))));
    }

    /// <summary>A copy of a kept output, or null when there is none whole: never kept, half written, or removed meanwhile.</summary>
    private static string? TryCopy(string cached)
    {
        try
        {
            return File.Exists(Path.Combine(cached, Complete)) ? CopyOf(Path.Combine(cached, Tree)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string CopyOf(string tree)
    {
        var copy = Path.Combine(Folders.NewTemp("staging"), Tree);
        Folders.Copy(tree, copy);
        return copy;
    }

    /// <summary>
    /// Moved in whole and marked complete, so a run that reads the cache never sees half an output. A second writer of
    /// the same key, or a file the system holds, leaves the output uncached.
    /// </summary>
    private static void Keep(string built, string cached)
    {
        var pending = cached + "-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Folders.Copy(built, Path.Combine(pending, Tree));
            File.WriteAllText(Path.Combine(pending, Complete), "");
            Directory.Move(pending, cached);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(pending);
        }
    }

    /// <summary>An old output is first renamed, so a run reading it fails whole and generates again, then removed.</summary>
    private static void RemoveOld()
    {
        Directory.CreateDirectory(Folder);
        foreach (var old in new DirectoryInfo(Folder).EnumerateDirectories()
            .Where(d => d.Name.Contains(Removed, StringComparison.Ordinal) || DateTime.UtcNow - d.CreationTimeUtc > Kept))
        {
            var doomed = old.Name.Contains(Removed, StringComparison.Ordinal) ? old.FullName : old.FullName + Removed + Guid.NewGuid().ToString("N")[..8];
            try
            {
                if (doomed != old.FullName)
                {
                    old.MoveTo(doomed);
                }

                Directory.Delete(doomed, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
