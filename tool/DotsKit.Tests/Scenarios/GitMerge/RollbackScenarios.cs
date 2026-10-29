using DotsKit.Io;
using DotsKit.Merging;
using static DotsKit.Tests.Scenarios.GitMerge.GitSolution;

namespace DotsKit.Tests.Scenarios.GitMerge;

/// <summary>
/// A merge by git that cannot go on leaves git as it was before the run: a second merge over one in progress is
/// refused, a step that fails after the merge and a merge git refuses put back the commit that adopted the base and
/// the branch dotskit/template.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class RollbackScenarios
{
    [Test]
    public async Task AMergeInProgress_IsRefused_AndAnAbortPutsBackWhatWasStagedBesideTheMerge()
    {
        var root = await SolutionAsync();
        var merge = new GitTemplateMerge(Runner, Sandbox.Self);
        await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((ProgramFile, NextProgram)), "2.0.0"), false, CancellationToken.None);
        Write(root, Solution, "All.sln with the template's projects\n");
        await merge.StageAsync(root, [Solution], CancellationToken.None);

        (await Should.ThrowAsync<InvalidOperationException>(() =>
            merge.MergeAsync(root, new(Base(), "1.0.0"), new(Base(), "2.0.0"), false, CancellationToken.None))).Message.ShouldContain("in progress");
        await merge.AbortAsync(root, CancellationToken.None);

        Read(root, Solution).ShouldBe("the team's own All.sln\n");
        Read(root, ProgramFile).ShouldBe(TeamsProgram);
    }

    [Test]
    public async Task AStepThatFailsAfterTheMerge_LeavesNoBranchAndNoCommitOfTheRun()
    {
        var root = await SolutionAsync();
        var head = await HeadAsync(root);
        var merge = new GitTemplateMerge(Runner, Sandbox.Self);
        await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((ProgramFile, NextProgram)), "2.0.0"), false, CancellationToken.None);

        await merge.AbortAsync(root, CancellationToken.None);

        (await HeadAsync(root)).ShouldBe(head);
        (await Runner.RunAsync("git", ["rev-parse", "-q", "--verify", "refs/heads/" + GitTemplateMerge.Branch], root, CancellationToken.None)).Code.ShouldNotBe(0);
        (await Runner.RunCheckedAsync("git", ["status", "--porcelain"], root, CancellationToken.None)).ShouldBeEmpty();
        Read(root, ProgramFile).ShouldBe(TeamsProgram);
    }

    [Test]
    public async Task AMergeGitRefuses_PutsBackTheCommitAndTheBranchOfTheRun_AndSaysSo()
    {
        const string added = "src/new.cs";
        var root = await SolutionAsync();
        var head = await HeadAsync(root);
        Write(root, added, "the team's file, not in git yet\n");
        var next = Tree((".gitignore", "appsettings.Secrets.json\n"), (ProgramFile, BaseProgram), (added, "the template's\n"));

        var refused = await Should.ThrowAsync<InvalidOperationException>(() =>
            new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Base(), "1.0.0"), new(next, "2.0.0"), false, CancellationToken.None));

        refused.Message.ShouldContain("put back");
        (await HeadAsync(root)).ShouldBe(head);
        (await Runner.RunAsync("git", ["rev-parse", "-q", "--verify", "refs/heads/" + GitTemplateMerge.Branch], root, CancellationToken.None)).Code.ShouldNotBe(0);
        Read(root, added).ShouldBe("the team's file, not in git yet\n");
    }
}
