namespace DotsKit.Reading;

/// <summary>The files of a solution as the reading of its manifest sees them, by paths relative to its root with forward slashes.</summary>
internal interface ISolutionFiles
{
    /// <summary>Whether a file or a folder is there. The last segment may end with <c>*</c>: any name it starts.</summary>
    bool Exists(string path);

    /// <summary>The text of a file, or null when there is none.</summary>
    string? Read(string path);

    /// <summary>The names of the folders inside <paramref name="path"/>, in ordinal order; none when it is not there.</summary>
    IReadOnlyList<string> Folders(string path);
}

internal sealed class DiskSolutionFiles(string root) : ISolutionFiles
{
    private const char Any = '*';

    public bool Exists(string path)
    {
        var full = Full(path);
        if (!full.EndsWith(Any))
        {
            return File.Exists(full) || Directory.Exists(full);
        }

        var folder = Path.GetDirectoryName(full);
        return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder, Path.GetFileName(full)).Any();
    }

    public string? Read(string path) => File.Exists(Full(path)) ? File.ReadAllText(Full(path)) : null;

    public IReadOnlyList<string> Folders(string path) =>
        Directory.Exists(Full(path))
            ? [.. Directory.GetDirectories(Full(path)).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];

    private string Full(string path) => Path.Combine(root, path);
}
