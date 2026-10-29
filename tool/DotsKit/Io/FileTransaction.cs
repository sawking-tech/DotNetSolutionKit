namespace DotsKit.Io;

/// <summary>
/// Writes and deletes the files of a plan as one step. Every new content is first written beside its file; only then
/// are the files swapped in, each original kept aside, and a failure of a write, a swap or a step the caller runs
/// inside the transaction puts the originals back. A process killed in the middle leaves the files beside the
/// solution's, *.dotskit-new and *.dotskit-old, for git status to show.
/// </summary>
internal sealed class FileTransaction(string root)
{
    private const string NewSuffix = ".dotskit-new";
    private const string OldSuffix = ".dotskit-old";

    private readonly Dictionary<string, byte[]?> _pending = new(StringComparer.Ordinal);
    private readonly List<string> _guarded = [];
    private readonly List<string> _createdFolders = [];

    public void Write(string relativePath, byte[] content) => _pending[relativePath] = content;

    public void Delete(string relativePath) => _pending[relativePath] = null;

    /// <summary>A file a step inside the transaction changes in place (All.sln under dotnet sln): kept aside, put back on failure.</summary>
    public void Guard(string relativePath) => _guarded.Add(relativePath);

    /// <summary>
    /// Writes everything, then runs <paramref name="inside"/>; a failure puts every file back. A file that cannot be
    /// put back does not stop the others, and the error names it beside its original. Once written, the cleanup of
    /// the originals and the emptied folders never fails the write: what it leaves, git status shows.
    /// </summary>
    public async Task CommitAsync(Func<Task> inside)
    {
        var swapped = new List<(string Target, bool Existed)>();
        try
        {
            StageNewContents();
            SwapIn(swapped);
            KeepAside(swapped);
            await inside();
        }
        catch (Exception cause)
        {
            var notBack = RollBack(swapped);
            BestEffort(RemoveStaged);
            BestEffort(RemoveCreatedFolders);
            if (notBack.Count > 0)
            {
                throw new InvalidOperationException($"{cause.Message} {NotBack(notBack)}", cause);
            }

            throw;
        }
        finally
        {
            BestEffort(RemoveStaged);
        }
        DropKeptOriginals(swapped);
        BestEffort(RemoveEmptiedFolders);
    }

    /// <summary>A cleanup the disk refuses, as a file another process holds, leaves the file for git status to show.</summary>
    private static void BestEffort(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The folders a deletion left empty, up to the root, as a project the template dropped.</summary>
    private void RemoveEmptiedFolders()
    {
        foreach (var path in _pending.Where(p => p.Value is null).Select(p => Full(p.Key)))
        {
            for (var folder = Path.GetDirectoryName(path); IsBelowRoot(folder) && !Directory.EnumerateFileSystemEntries(folder!).Any(); folder = Path.GetDirectoryName(folder))
            {
                Directory.Delete(folder!);
            }
        }
    }

    private bool IsBelowRoot(string? folder) =>
        folder is not null && Directory.Exists(folder)
        && !string.Equals(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private void StageNewContents()
    {
        foreach (var (path, content) in _pending.Where(p => p.Value is not null))
        {
            var target = Full(path);
            CreateFolderOf(target);
            File.WriteAllBytes(target + NewSuffix, content!);
        }
    }

    /// <summary>Creates the folder of <paramref name="target"/>, remembering each folder it had to create, outermost first.</summary>
    private void CreateFolderOf(string target)
    {
        var missing = new Stack<string>();
        for (var folder = Path.GetDirectoryName(target); folder is not null && !Directory.Exists(folder); folder = Path.GetDirectoryName(folder))
        {
            missing.Push(folder);
        }

        _createdFolders.AddRange(missing);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    }

    /// <summary>The folders a failed transaction created, innermost first, so the solution has no empty folders left.</summary>
    private void RemoveCreatedFolders()
    {
        foreach (var folder in Enumerable.Reverse(_createdFolders).Where(f => Directory.Exists(f) && !Directory.EnumerateFileSystemEntries(f).Any()))
        {
            Directory.Delete(folder);
        }
    }

    private void SwapIn(List<(string Target, bool Existed)> swapped)
    {
        foreach (var (path, content) in _pending)
        {
            var target = Full(path);
            var existed = File.Exists(target);
            if (existed)
            {
                File.Move(target, target + OldSuffix, overwrite: true);
            }

            swapped.Add((target, existed));
            if (content is not null)
            {
                File.Move(target + NewSuffix, target);
            }
        }
    }

    private void KeepAside(List<(string Target, bool Existed)> swapped)
    {
        foreach (var target in _guarded.Select(Full).Where(File.Exists))
        {
            File.Copy(target, target + OldSuffix, overwrite: true);
            swapped.Add((target, true));
        }
    }

    /// <summary>What to do with the files the disk did not let the rollback put back: an original to move over, a new file to delete.</summary>
    private static string NotBack(List<(string Target, bool Existed)> notBack)
    {
        var changed = notBack.Where(f => f.Existed).Select(f => f.Target).ToList();
        var added = notBack.Where(f => !f.Existed).Select(f => f.Target).ToList();
        return string.Join(' ',
            changed.Count > 0 ? $"These files could not be put back: {string.Join(", ", changed)}; each original is beside it as {OldSuffix}: move it over the file." : "",
            added.Count > 0 ? $"These new files could not be removed: {string.Join(", ", added)}; delete them." : "").Trim();
    }

    /// <summary>In reverse order, so a file swapped twice ends as it began. Returns the files the disk refused.</summary>
    private static List<(string Target, bool Existed)> RollBack(List<(string Target, bool Existed)> swapped)
    {
        var notBack = new List<(string Target, bool Existed)>();
        foreach (var (target, existed) in Enumerable.Reverse(swapped))
        {
            try
            {
                if (existed && File.Exists(target + OldSuffix))
                {
                    File.Move(target + OldSuffix, target, overwrite: true);
                }
                else if (!existed && File.Exists(target))
                {
                    File.Delete(target);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                notBack.Add((target, existed));
            }
        }
        return notBack;
    }

    private void RemoveStaged()
    {
        foreach (var path in _pending.Keys.Select(Full).Where(p => File.Exists(p + NewSuffix)))
        {
            File.Delete(path + NewSuffix);
        }
    }

    private static void DropKeptOriginals(List<(string Target, bool Existed)> swapped)
    {
        foreach (var (target, _) in swapped.Where(s => File.Exists(s.Target + OldSuffix)))
        {
            BestEffort(() => File.Delete(target + OldSuffix));
        }
    }

    private string Full(string relativePath) => Path.Combine(root, relativePath);
}
