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
    public async Task AnAbort_PutsBackATrackedFileALaterStepChanged_WithoutStagingIt()
    {
        var root = await SolutionAsync();
        var head = await HeadAsync(root);
        var merge = new GitTemplateMerge(Runner, Sandbox.Self);
        await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((ProgramFile, NextProgram)), "2.0.0"), false, CancellationToken.None);
        Write(root, Solution, "All.sln a repair changed and did not stage\n");

        await merge.AbortAsync(root, CancellationToken.None);

        (await HeadAsync(root)).ShouldBe(head);
        Read(root, Solution).ShouldBe("the team's own All.sln\n");
        (await Runner.RunCheckedAsync("git", ["status", "--porcelain"], root, CancellationToken.None)).ShouldBeEmpty();
    }

    [Test]
    public async Task AnAbortGitRefuses_NamesTheLockAGitStoppedLeft_AndTheCommandsThatPutTheRunBack()
    {
        var root = await SolutionAsync();
        var head = await HeadAsync(root);
        var merge = new GitTemplateMerge(Runner, Sandbox.Self);
        await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((ProgramFile, NextProgram)), "2.0.0"), false, CancellationToken.None);
        var lockFile = Path.Combine(root, ".git", "index.lock");
        await File.WriteAllTextAsync(lockFile, "");

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => merge.AbortAsync(root, CancellationToken.None));
        File.Delete(lockFile);

        refused.Message.ShouldContain("index.lock");
        refused.Message.ShouldContain($"git reset --hard {head}");
        refused.Message.ShouldContain("update-ref -d refs/heads/" + GitTemplateMerge.Branch);
    }

    [Test]
    public async Task AFileTheTeamAddedAfterTheCommandStarted_StopsTheMergeBeforeIt_AndStays()
    {
        const string added = "src/new.cs";
        var root = await SolutionAsync();
        var head = await HeadAsync(root);
        Write(root, added, "the team's file, not in git yet\n");
        var next = Tree((".gitignore", "appsettings.Secrets.json\n"), (ProgramFile, BaseProgram), (added, "the template's\n"));

        var refused = await Should.ThrowAsync<InvalidOperationException>(() =>
            new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Base(), "1.0.0"), new(next, "2.0.0"), false, CancellationToken.None));

        refused.Message.ShouldContain("changed after the command started");
        (await HeadAsync(root)).ShouldBe(head);
        (await Runner.RunAsync("git", ["rev-parse", "-q", "--verify", "refs/heads/" + GitTemplateMerge.Branch], root, CancellationToken.None)).Code.ShouldNotBe(0);
        Read(root, added).ShouldBe("the team's file, not in git yet\n");
    }

    [Test]
    public async Task AChangeTheTeamMadeWhileThePlanWaited_StopsTheMergeBeforeIt_AndStays()
    {
        var root = await SolutionAsync();
        var head = await HeadAsync(root);
        Write(root, ProgramFile, "the team's change, saved while the plan waited for a yes\n");

        var refused = await Should.ThrowAsync<InvalidOperationException>(() =>
            new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Base(), "1.0.0"), new(Tree((ProgramFile, NextProgram)), "2.0.0"), false, CancellationToken.None));

        refused.Message.ShouldContain($"changed after the command started (M {ProgramFile})");
        (await HeadAsync(root)).ShouldBe(head);
        (await Runner.RunAsync("git", ["rev-parse", "-q", "--verify", "refs/heads/" + GitTemplateMerge.Branch], root, CancellationToken.None)).Code.ShouldNotBe(0);
        Read(root, ProgramFile).ShouldBe("the team's change, saved while the plan waited for a yes\n");
    }

    /// <summary>The merge by git, whose staging writes and then fails, as a git add the disk refuses.</summary>
    private sealed class FailingAfterStage(IGitMerge git) : IGitMerge
    {
        public IReadOnlySet<string> LeftOut => git.LeftOut;

        public Task<IReadOnlyList<string>> MergeAsync(string root, TemplateCommit @base, TemplateCommit next, bool preferTemplate, CancellationToken ct) =>
            git.MergeAsync(root, @base, next, preferTemplate, ct);

        public async Task StageAsync(string root, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await git.StageAsync(root, paths, ct);
            throw new IOException("the disk is full");
        }

        public Task AbortAsync(string root, CancellationToken ct) => git.AbortAsync(root, ct);
    }

    private sealed class NoBuild : Solutions.IBuilder
    {
        public Task<ProcessResult> BuildAsync(string root, CancellationToken ct) => Task.FromResult(new ProcessResult(0, "", ""));
    }

    [Test]
    public async Task AStageThatFails_OverAManifestInGit_LeavesNoMergeNoCommitAndTheManifestAsItWas()
    {
        var root = await SolutionAsync();
        const string described = """{ "template": "1.0.0", "solution": ["--NamespaceRoot", "Acme", "--ProductName", "Shop"], "services": [] }""";
        Write(root, Layout.ManifestPath, described + "\n");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "described by dotskit init");
        var head = await HeadAsync(root);
        var manifest = new Manifest { Template = "2.0.0", Solution = ["--NamespaceRoot", "Acme", "--ProductName", "Shop"], Services = [] };
        var plan = new Planning.Plan(root, manifest, [], new Solutions.MembershipChange([], []),
            ByGit: (new TemplateCommit(Base(), "1.0.0"), new TemplateCommit(Tree((ProgramFile, NextProgram)), "2.0.0")));
        var writer = new Planning.PlanWriter(new Solutions.SolutionProjects(Runner), new FailingAfterStage(new GitTemplateMerge(Runner, Sandbox.Self)),
            new Solutions.ReferenceRepair(new Solutions.RepairWriter(), new NoBuild()));

        await Should.ThrowAsync<IOException>(() => writer.WriteAsync(plan, CancellationToken.None));

        (await HeadAsync(root)).ShouldBe(head);
        (await Runner.RunAsync("git", ["rev-parse", "-q", "--verify", "MERGE_HEAD"], root, CancellationToken.None)).Code.ShouldNotBe(0);
        (await Runner.RunCheckedAsync("git", ["status", "--porcelain"], root, CancellationToken.None)).ShouldBeEmpty();
        Read(root, Layout.ManifestPath).ShouldBe(described + "\n");
    }
}
