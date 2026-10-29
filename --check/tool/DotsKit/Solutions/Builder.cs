using DotsKit.Io;

namespace DotsKit.Solutions;

/// <summary>Builds a solution after the tool wrote it: a command ends with a solution that builds.</summary>
internal interface IBuilder
{
    Task<ProcessResult> BuildAsync(string root, CancellationToken ct);
}

internal sealed class DotnetBuilder(IProcessRunner runner) : IBuilder
{
    public Task<ProcessResult> BuildAsync(string root, CancellationToken ct) =>
        // The compiler server is let run, a third of a build; MSBuild's own nodes and server stay off (ProcessRunner).
        runner.RunAsync("dotnet", ["build", Layout.SolutionFileOf(root), "-nologo", "-v", "q"], root, ct);
}
