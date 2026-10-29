using System.Security.Cryptography;
using System.Text;

namespace DotsKit.Io;

/// <summary>
/// One command at a time in a solution: two would mix their writes, move the branch dotskit/template under each
/// other and build into one obj. The lock is a file in the temp folder, keyed by the solution's path, so the
/// solution's tree stays clean, held open with no sharing for the whole command: Windows refuses a second open,
/// Linux and macOS take it as flock, and either way the system lets it go when the process ends, killed or not.
/// </summary>
internal static class SolutionLock
{
    private static readonly string Folder = Path.Combine(Folders.RealPath(Path.GetTempPath()), "dotskit", "locks");

    /// <summary>Hex characters of the path's hash in the lock's name: enough to tell solutions apart.</summary>
    private const int KeyLength = 16;

    /// <summary>The lock of the solution at <paramref name="root"/>, or null when another command holds it.</summary>
    public static IDisposable? TryTake(string root)
    {
        Directory.CreateDirectory(Folder);
        try
        {
            return new FileStream(Path.Combine(Folder, Key(root) + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>The real path, in one case on Windows, where C:\A and c:\a are one folder.</summary>
    private static string Key(string root)
    {
        var path = Folders.RealPath(root).TrimEnd('/', '\\');
        if (OperatingSystem.IsWindows())
        {
            path = path.ToUpperInvariant();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..KeyLength];
    }
}
