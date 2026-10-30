using System.Text.RegularExpressions;
using DotsKit.Io;

namespace DotsKit.Generation;

/// <summary>The releases of the template, by the tags of its repository: v1.0.0, v2.6.2; a pre-release has no tag.</summary>
internal interface ITemplateReleases
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct);
}

/// <summary>
/// <c>git ls-remote --tags</c> of the repository: no clone, and the tags are where the releases before nuget.org are
/// taken from as well (<see cref="TagArchive"/>). Every git command here is listed in docs/getting-started/tool.md,
/// "What dotskit runs in git": a command added or changed here goes there too.
/// </summary>
internal sealed partial class GitTemplateReleases(IProcessRunner runner) : ITemplateReleases
{
    [GeneratedRegex(@"refs/tags/v(?<version>\d+\.\d+\.\d+)\r?$", RegexOptions.Multiline)]
    private static partial Regex Release();

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct)
    {
        var listed = await runner.RunAsync("git", ["ls-remote", "--tags", "--refs", Repository.Url], Path.GetTempPath(), ct);
        return listed.Code == 0
            ? Parse(listed.Output)
            : throw new InvalidOperationException(
                $"The releases of the template could not be listed from {Repository.Url}: check the network, or name the version, dotskit init --template-version 2.x.y; a solution of 1.x is brought to 2.0.0 first, by docs/getting-started/upgrading.md, \"A solution of v1\".");
    }

    /// <summary>The versions of the release tags in the output of <c>git ls-remote --tags</c>.</summary>
    public static IReadOnlyList<string> Parse(string listed) => [.. Release().Matches(listed).Select(m => m.Groups["version"].Value)];
}
