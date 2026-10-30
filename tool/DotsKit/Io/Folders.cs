namespace DotsKit.Io;

internal static class Folders
{
    /// <summary>The folder of this run: every temporary folder is inside it, and it goes when the run ends.</summary>
    private static readonly string Run = Path.Combine(OwnFolder("runs"), Guid.NewGuid().ToString("N")[..8]);

    /// <summary>
    /// A folder of the tool's own under the user's local data, made for the user alone on Linux and macOS. Not the
    /// system's temp: /tmp is shared between the users of a Linux host, and a shared, predictable path would let
    /// another user plant a cached output, swap a run's files or hold a lock.
    /// </summary>
    public static string OwnFolder(string purpose)
    {
        var path = Path.Combine(RealPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create)), "dotskit", purpose);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

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
    /// The path with its links resolved is <paramref name="root"/> or below it: a committed link must not lead a
    /// write or a read of the solution's files outside it.
    /// </summary>
    public static bool IsInside(string root, string path)
    {
        var realRoot = RealPath(root).TrimEnd(Path.DirectorySeparatorChar);
        // A path a file has yet to take: resolved down to the folders that exist, since the rest holds no link.
        // The deepest existing folder is resolved itself, so a folder that is a link is seen as what it points at.
        var full = Path.GetFullPath(path);
        var existing = Existing(full);
        var real = Path.Combine(RealPath(existing), Path.GetRelativePath(existing, full)).TrimEnd(Path.DirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(real, realRoot, comparison) || real.StartsWith(realRoot + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>The deepest folder of the path that exists; the folders below it are yet to be created.</summary>
    private static string Existing(string folder)
    {
        var current = folder;
        while (!Directory.Exists(current) && Path.GetDirectoryName(current) is { } parent)
        {
            current = parent;
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
        FilesOf(root).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.Split('/').Any(Layout.Ignored.Contains));

    /// <summary>
    /// Every file of a tree, by full path, without descending into a folder that is a link: a committed link would
    /// otherwise put the files of another place - the user's other repository - among the solution's own, where a
    /// repair would rewrite them outside anything git can undo.
    /// </summary>
    public static IEnumerable<string> FilesOf(string root)
    {
        var folders = new Stack<string>([root]);
        while (folders.Count > 0)
        {
            var folder = folders.Pop();
            foreach (var child in Directory.EnumerateDirectories(folder))
            {
                if (new DirectoryInfo(child).LinkTarget is null && !Layout.Ignored.Contains(Path.GetFileName(child)))
                {
                    folders.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                yield return file;
            }
        }
    }
}
