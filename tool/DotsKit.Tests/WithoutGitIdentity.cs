using DotsKit.Io;

namespace DotsKit.Tests;

/// <summary>
/// A runner under which git sees no global or system config: a repository with nothing configured has no identity.
/// </summary>
internal sealed class WithoutGitIdentity(IProcessRunner inner) : IProcessRunner
{
    private static readonly IReadOnlyDictionary<string, string> Isolated =
        new Dictionary<string, string> { ["GIT_CONFIG_GLOBAL"] = EmptyGlobalConfig(), ["GIT_CONFIG_NOSYSTEM"] = "1" };

    public async Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> args, string workingDirectory, CancellationToken ct,
        IReadOnlyDictionary<string, string>? environment = null, string? input = null)
    {
        var merged = new Dictionary<string, string>(Isolated);
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                merged[key] = value;
            }
        }

        return await inner.RunAsync(program, args, workingDirectory, ct, merged, input);
    }

    private static string EmptyGlobalConfig()
    {
        var path = Path.Combine(Folders.NewTemp("git-config"), "global");
        File.WriteAllText(path, "");
        return path;
    }
}
