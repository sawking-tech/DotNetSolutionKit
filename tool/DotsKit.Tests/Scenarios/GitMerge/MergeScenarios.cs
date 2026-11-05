using DotsKit.Io;
using DotsKit.Merging;
using static DotsKit.Tests.Scenarios.GitMerge.GitSolution;

namespace DotsKit.Tests.Scenarios.GitMerge;

/// <summary>
/// The template's output merged into a solution by git, through the branch dotskit/template: the team's changes
/// stay, a line both changed is a conflict for the IDE, what the solution's git ignores and All.sln are never in the
/// branch, and the base is adopted again on a branch that has not got it.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class MergeScenarios
{
    [Test]
    public async Task AFileBothAdded_GainsTheSolutionsSideAsBase_SoTheIDEShowsItsPanes()
    {
        var root = await SolutionAsync();
        const string both = "src/services/Acme.Shop.Orders/notes.md";
        Write(root, both, "the team's notes\n");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the team's notes");

        var conflicts = await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Base(), "1.0.0"),
            new(Tree((ProgramFile, NextProgram), (both, "the template's notes\n")), "2.0.0"), false, false, CancellationToken.None);

        conflicts.ShouldBe([both]);
        var unmerged = await Runner.RunCheckedAsync("git", ["ls-files", "-u", "--", both], root, CancellationToken.None);
        unmerged.Trim().Split('\n').Select(line => line.Split('\t')[0].Split(' ')[2]).Order().ShouldBe(["1", "2", "3"]);
        // The base the conflict gained is the solution's own side: the IDE opens the file with its merge view -
        // the result starts as the solution's, the template's side shows as the change that overwrites it.
        (await Runner.RunCheckedAsync("git", ["cat-file", "-p", $":1:{both}"], root, CancellationToken.None)).ShouldBe("the team's notes\n");
    }

    [Test]
    public async Task AConflictIsMarkedWithItsBase_When_Zdiff3IsNamed()
    {
        var root = await SolutionAsync();
        Write(root, ProgramFile, "a\nteam\nc\nd\ne\nteam\n");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the team's line");

        var conflicts = await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Base(), "1.0.0"),
            new(Tree((ProgramFile, "a\ntemplate\nc\nd\ne\n")), "2.0.0"), false, true, CancellationToken.None);

        conflicts.ShouldBe([ProgramFile]);
        Read(root, ProgramFile).ShouldContain("team\n||||||| base");
    }

    [Test]
    public async Task AFileBothAdded_ResolvedAndCommitted_LeavesTheIndexCleanForTheNextRun()
    {
        var root = await SolutionAsync();
        const string both = "src/services/Acme.Shop.Orders/notes.md";
        Write(root, both, "the team's notes\n");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the team's notes");

        await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Base(), "1.0.0"),
            new(Tree((".gitignore", "appsettings.Secrets.json\n"), (ProgramFile, NextProgram), (Solution, "the next All.sln\n"), (both, "the template's notes\n")), "2.0.0"), false, false, CancellationToken.None);
        await Git(root, "checkout", "--theirs", "--", both);
        await Git(root, "add", both);
        await Git(root, "commit", "-q", "--no-edit");

        // The base the conflict gained is gone with the resolution: nothing unmerged, nothing staged, nothing dirty.
        (await Runner.RunCheckedAsync("git", ["ls-files", "-u"], root, CancellationToken.None)).ShouldBe("");
        (await Runner.RunCheckedAsync("git", ["status", "--porcelain"], root, CancellationToken.None)).ShouldBe("");
        Read(root, both).ShouldBe("the template's notes\n");
    }

    [Test]
    public async Task HooksThatRefuseEveryCommit_DoNotStopTheBaseFromBeingAdopted()
    {
        var root = await SolutionAsync();
        var head = await HeadAsync(root);
        // The repository's own hooks folder, over a global core.hooksPath that would leave these hooks unread.
        await Git(root, "config", "core.hooksPath", ".git/hooks");
        foreach (var hook in new[] { "commit-msg", "pre-merge-commit", "prepare-commit-msg", "post-merge" })
        {
            var path = Path.Combine(root, ".git", "hooks", hook);
            await File.WriteAllTextAsync(path, "#!/bin/sh\necho refused by the team's hook >&2\nexit 1\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        var conflicts = await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Base(), "1.0.0"), new(Tree((ProgramFile, NextProgram)), "2.0.0"), false, false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        Read(root, ProgramFile).ShouldBe(NextProgram + "team\n");
        (await Runner.RunCheckedAsync("git", ["rev-parse", "HEAD^1"], root, CancellationToken.None)).Trim().ShouldBe(head);
        (await Runner.RunCheckedAsync("git", ["log", "-1", "--format=%s"], root, CancellationToken.None)).Trim().ShouldBe("dotskit: the solution is of the template 1.0.0");
    }

    [Test]
    public async Task TheTemplatesChange_MergesWithTheTeams_AndAnIgnoredFileAndAllSlnStayAsTheyAre()
    {
        var root = await SolutionAsync();
        var next = Tree((".gitignore", "appsettings.Secrets.json\n"), (ProgramFile, NextProgram), (Solution, "the next All.sln\n"), (Secrets, "the next secret\n"));

        var conflicts = await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Base(), "1.0.0"), new(next, "2.0.0"), false, false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        Read(root, ProgramFile).ShouldBe(NextProgram + "team\n");
        Read(root, Secrets).ShouldBe("the team's secret\n");
        Read(root, Solution).ShouldBe("the team's own All.sln\n");
        (await Runner.RunCheckedAsync("git", ["ls-tree", "-r", "--name-only", GitTemplateMerge.Branch], root, CancellationToken.None))
            .ShouldNotContain("appsettings.Secrets.json");
    }

    [Test]
    public async Task ABranchWhoseBaseTheWorkingBranchHasNot_IsNotMergedOverTheSolution_TheBaseIsAdoptedAgain()
    {
        var root = await SolutionAsync();
        await Git(root, "branch", "main-before");
        var merge = new GitTemplateMerge(Runner, Sandbox.Self);
        await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Base(), "1.0.0"), false, false, CancellationToken.None);
        await Git(root, "commit", "-q", "--allow-empty", "-m", "adopted on this branch");
        await Git(root, "checkout", "-q", "main-before");

        var conflicts = await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((ProgramFile, NextProgram)), "2.0.0"), false, false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        Read(root, ProgramFile).ShouldBe(NextProgram + "team\n");
    }

    [Test]
    public async Task ASecretANestedGitignoreHides_IsNotBroughtByTheTemplate()
    {
        var root = await SolutionAsync();
        const string local = "src/services/Acme.Shop.Orders/appsettings.Local.json";
        Write(root, "src/services/Acme.Shop.Orders/.gitignore", "appsettings.Local.json\n");
        Write(root, local, "the team's local secret\n");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "a nested ignore");

        await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Base(), "1.0.0"), new(Tree((local, "the template's local file\n")), "2.0.0"), false, false, CancellationToken.None);

        Read(root, local).ShouldBe("the team's local secret\n");
    }

    /// <summary>The .gitignore of the base, kept by the next outputs: one the template dropped would leave the secrets file untracked.</summary>
    private static readonly (string Path, string Text) Ignore = (".gitignore", "appsettings.Secrets.json\n");

    [Test]
    public async Task ALineBothChanged_IsAConflictGitMarks_AndTheNextUpgradeStartsFromTheBranch()
    {
        var root = await SolutionAsync();
        Write(root, ProgramFile, "a\nb\nC by the team\nd\ne\nteam\n");
        await Git(root, "commit", "-q", "-am", "the team's change");
        var merge = new GitTemplateMerge(Runner, Sandbox.Self);

        var conflicts = await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree(Ignore, (ProgramFile, "a\nb\nC by the template\nd\ne\n")), "2.0.0"), false, false, CancellationToken.None);

        conflicts.ShouldBe([ProgramFile]);
        Read(root, ProgramFile).ShouldContain("<<<<<<<");
        await Git(root, "merge", "--abort");
        Read(root, ProgramFile).ShouldBe("a\nb\nC by the team\nd\ne\nteam\n");

        // Resolved the template's way and committed, the next version merges over 2.0.0, not over 1.0.0 again.
        await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree(Ignore, (ProgramFile, "a\nb\nC by the template\nd\ne\n")), "2.0.0"), true, false, CancellationToken.None);
        await Git(root, "commit", "-q", "--no-edit");
        var again = await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree(Ignore, (ProgramFile, "new in 3.0\na\nb\nC by the template\nd\ne\n")), "3.0.0"), false, false, CancellationToken.None);

        again.ShouldBeEmpty();
        Read(root, ProgramFile).ShouldBe("new in 3.0\na\nb\nC by the template\nd\ne\nteam\n");
    }

    [TestCase("a  \r\nb\r\n", "a\nb\n", false)]
    [TestCase("a\nb\n", "a\nB by the template\n", true)]
    public async Task AFileTheTeamRemoved_StaysRemoved_WhenTheTemplateOnlyRestyledIt(string before, string after, bool conflicts)
    {
        const string removed = "src/services/Acme.Shop.Orders/Removed.cs";
        var root = await SolutionAsync();
        (string, string)[] common = [(".gitignore", "appsettings.Secrets.json\n"), (ProgramFile, BaseProgram), (Solution, "the template's All.sln\n"), (Secrets, "the template's secret\n")];
        var baseTree = Tree([.. common, (removed, before)]);
        var next = Tree([.. common, (removed, after)]);

        var left = await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(baseTree, "1.0.0"), new(next, "2.0.0"), false, false, CancellationToken.None);

        left.Contains(removed).ShouldBe(conflicts);
        File.Exists(Path.Combine(root, removed)).ShouldBe(conflicts);
    }

    [Test]
    public async Task AFileTheTemplateRemoved_TheTeamChanged_FollowsTheTemplate_When_Force_AndIsNamed()
    {
        var root = await SolutionAsync();
        var merge = new GitTemplateMerge(Runner, Sandbox.Self);

        var conflicts = await merge.MergeAsync(root, new(Base(), "1.0.0"),
            new(Tree((Solution, "the template's All.sln\n")), "2.0.0"), true, false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        File.Exists(Path.Combine(root, ProgramFile)).ShouldBeFalse();
        merge.Taken.ShouldBe([ProgramFile]);
    }

    [Test]
    public async Task AFileTheTeamRemoved_TheTemplateChanged_FollowsTheTemplate_When_Force_AndIsNamed()
    {
        var root = await SolutionAsync();
        File.Delete(Path.Combine(root, ProgramFile));
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the team removed the file");
        var merge = new GitTemplateMerge(Runner, Sandbox.Self);

        var conflicts = await merge.MergeAsync(root, new(Base(), "1.0.0"),
            new(Tree((ProgramFile, NextProgram), (Solution, "the template's All.sln\n")), "2.0.0"), true, false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        Read(root, ProgramFile).ShouldBe(NextProgram);
        merge.Taken.ShouldBe([ProgramFile]);
    }

    [Test]
    public async Task ALinesTheTeamWrote_GiveWayToTheTemplate_When_Force_AndTheFileIsNamed()
    {
        var root = await SolutionAsync();
        var merge = new GitTemplateMerge(Runner, Sandbox.Self);

        var conflicts = await merge.MergeAsync(root, new(Base(), "1.0.0"),
            new(Tree((ProgramFile, "a\nb\nc\nd\ne\ntemplate\n"), (Solution, "the template's All.sln\n")), "2.0.0"), true, false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        Read(root, ProgramFile).ShouldBe("a\nb\nc\nd\ne\ntemplate\n");
        merge.Taken.ShouldBe([ProgramFile]);
    }
}
