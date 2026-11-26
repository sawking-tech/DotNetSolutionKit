using DotsKit.Merging;

namespace DotsKit.Tests;

/// <summary>A style that changes nothing: the scenarios merge the bytes, without a formatter and without the team's file.</summary>
internal sealed class NoStyleFormat : IStyleFormat
{
    public Task<string> LaidOutAsync(string root, string tree, IReadOnlyCollection<string> paths, CancellationToken ct) => Task.FromResult(tree);
}
