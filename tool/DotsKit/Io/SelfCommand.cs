namespace DotsKit.Io;

/// <summary>
/// The command line that runs this tool again from the shell git starts a merge driver in: the app host as it is,
/// or the dotnet host with the tool's assembly. Forward slashes, quoted, so a path with a space or a backslash of
/// Windows reaches the shell whole.
/// </summary>
internal static class SelfCommand
{
    private const string DotnetHost = "dotnet";

    public static string Of(string? processPath, string assembly) =>
        processPath is null || Path.GetFileNameWithoutExtension(processPath) == DotnetHost
            ? $"{Quoted(processPath ?? DotnetHost)} {Quoted(assembly)}"
            : Quoted(processPath);

    private static string Quoted(string path) => $"\"{path.Replace('\\', '/')}\"";
}
