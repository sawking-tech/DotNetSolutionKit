namespace DotsKit.Io;

internal static class Folders
{
    /// <summary>The folder of this run: every temporary folder is inside it, and it goes when the run ends.</summary>
    private static readonly string Run = Path.Combine(RealPath(Path.GetTempPath()), "dotskit", Guid.NewGuid().ToString("N")[..8]);

    /// <summary>
    /// The path with every symbolic link on it resolved. On macOS the temp folder is under /var, a link to
    /// /private/var; dotnet sln resolves the link for one path and not for the other, and lists a project of
    /// the temp folder as ../../private/var/....
    /// </summary>
    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                current = target.FullName;
            }
        }
        return current;
    }

    /// <summary>
    /// Writes a file of the solution whole or not at all: the content goes beside it first and then takes its place, so
    /// a run stopped in the middle leaves the file as it was, never cut short.
    /// </summary>
    public static void WriteWhole(string path, byte[] content)
    {
        var beside = path + WriteSuffix;
        File.WriteAllBytes(beside, content);
        File.Move(beside, path, overwrite: true);
    }

    /// <summary>The suffix of a file being written beside the one it replaces; git status shows one a kill left.</summary>
    private const string WriteSuffix = ".dotskit-new";

    /// <summary>A new empty folder of this run; short, since a generated solution has deep paths.</summary>
    public static string NewTemp(string purpose)
    {
        var path = Path.Combine(Run, purpose + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Removes what this run left in the temp folder; a file still held stays for the system to clear.</summary>
    public static void RemoveRunFolders()
    {
        try
        {
            if (Directory.Exists(Run))
            {
                Directory.Delete(Run, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Copy(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// A new tree of <paramref name="baseDir"/>'s files, where every path <paramref name="fromNext"/> takes comes from
    /// <paramref name="nextDir"/> instead: a file there is written, one only in the base is left out.
    /// </summary>
    public static string Overlay(string baseDir, string nextDir, Func<string, bool> fromNext)
    {
        var tree = NewTemp("overlay");
        foreach (var (from, file) in RelativeFiles(baseDir).Where(f => !fromNext(f)).Select(f => (baseDir, f))
            .Concat(RelativeFiles(nextDir).Where(fromNext).Select(f => (nextDir, f))))
        {
            var target = Path.Combine(tree, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(from, file), target);
        }

        return tree;
    }

    /// <summary>The template's files of a tree, relative to it, with forward slashes.</summary>
    public static IEnumerable<string> RelativeFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Split('/').Any(Layout.Ignored.Contains));
}
