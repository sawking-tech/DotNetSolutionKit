using DotsKit.Io;
using DotsKit.Merging;
using static DotsKit.Tests.Scenarios.GitMerge.GitSolution;

namespace DotsKit.Tests.Scenarios.GitMerge;

/// <summary>
/// What the command stages beside the merge reaches git whole, however many files: the paths go in a file, not on a
/// command line, and Windows starts no process whose command line passes 32 767 characters.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class StagingScenarios
{
    /// <summary>More files than one command line of Windows holds: about 90 000 characters of paths.</summary>
    private const int Files = 2000;

    [Test]
    public async Task ThousandsOfFiles_AreStaged_PastTheLengthOfACommandLine()
    {
        var root = await SolutionAsync();
        var paths = Enumerable.Range(0, Files).Select(n => $"src/services/Acme.Shop.Orders/Repaired/AFileTheRepairChanged{n:D4}.cs").ToList();
        foreach (var path in paths)
        {
            Write(root, path, "class A;\n");
        }

        await new GitTemplateMerge(Runner, Sandbox.Self).StageAsync(root, paths, CancellationToken.None);

        (await Runner.RunCheckedAsync("git", ["diff", "--cached", "--name-only"], root, CancellationToken.None))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(Files);
    }
}
