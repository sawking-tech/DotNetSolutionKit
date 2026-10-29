using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.Tests.Scenarios;

/// <summary>
/// The template's output merged into a solution by git, through the branch dotskit/template, with the real git: the
/// team's changes stay, a change of both sides is a conflict for the IDE, and what the solution ignores is not touched.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class GitMergeScenarios
{
    private const string Program = "src/services/Acme.Shop.Orders/Program.cs";
    private const string Secrets = "src/services/Acme.Shop.Orders/appsettings.Secrets.json";
    private const string Solution = "src/services/Acme.Shop.All.sln";

    private static readonly ProcessRunner Runner = new();

    private static string Tree(params (string Path, string Text)[] files)
    {
        var root = Folders.NewTemp("git-merge-tree");
        foreach (var (path, text) in files)
            Write(root, path, text);
        return root;
    }

    private static void Write(string root, string path, string text)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>The text as LF: git writes a merged file by the machine's core.autocrlf.</summary>
    private static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static async Task Git(string root, params string[] args) => await Runner.RunCheckedAsync("git", args, root, CancellationToken.None);

    /// <summary>A solution as the template 1.0 gave it, committed, with a local secret git ignores and the team's line.</summary>
    private static async Task<string> SolutionAsync()
    {
        var root = Folders.NewTemp("git-merge-solution");
        Write(root, ".gitignore", "appsettings.Secrets.json\n");
        Write(root, Program, "a\nb\nc\nd\ne\nteam\n");
        Write(root, Solution, "the team's own All.sln\n");
        Write(root, Secrets, "the team's secret\n");
        await Git(root, "init", "-q");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the solution");
        return root;
    }

    private static string Base() =>
        Tree((".gitignore", "appsettings.Secrets.json\n"), (Program, "a\nb\nc\nd\ne\n"), (Solution, "the template's All.sln\n"), (Secrets, "the template's secret\n"));

    [Test]
    public async Task TheTemplatesChange_MergesWithTheTeams_AndAnIgnoredFileAndAllSlnStayAsTheyAre()
    {
        var root = await SolutionAsync();
        var next = Tree((".gitignore", "appsettings.Secrets.json\n"), (Program, "template\na\nb\nc\nd\ne\n"), (Solution, "the next All.sln\n"), (Secrets, "the next secret\n"));

        var conflicts = await new GitTemplateMerge(Runner).MergeAsync(root, new(Base(), "1.0.0"), new(next, "2.0.0"), false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        Read(root, Program).ShouldBe("template\na\nb\nc\nd\ne\nteam\n");
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
        var merge = new GitTemplateMerge(Runner);
        await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Base(), "1.0.0"), false, CancellationToken.None);
        await Git(root, "commit", "-q", "--allow-empty", "-m", "adopted on this branch");
        await Git(root, "checkout", "-q", "main-before");

        var conflicts = await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((Program, "template\na\nb\nc\nd\ne\n")), "2.0.0"), false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        Read(root, Program).ShouldBe("template\na\nb\nc\nd\ne\nteam\n");
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

        await new GitTemplateMerge(Runner).MergeAsync(root, new(Base(), "1.0.0"), new(Tree((local, "the template's local file\n")), "2.0.0"), false, CancellationToken.None);

        Read(root, local).ShouldBe("the team's local secret\n");
    }

    [Test]
    public async Task AMergeInProgress_IsRefused_AndAnAbortPutsBackWhatWasStagedBesideTheMerge()
    {
        var root = await SolutionAsync();
        var merge = new GitTemplateMerge(Runner);
        await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((Program, "template\na\nb\nc\nd\ne\n")), "2.0.0"), false, CancellationToken.None);
        Write(root, Solution, "All.sln with the template's projects\n");
        await merge.StageAsync(root, [Solution], CancellationToken.None);

        (await Should.ThrowAsync<InvalidOperationException>(() =>
            merge.MergeAsync(root, new(Base(), "1.0.0"), new(Base(), "2.0.0"), false, CancellationToken.None))).Message.ShouldContain("in progress");
        await merge.AbortAsync(root, CancellationToken.None);

        Read(root, Solution).ShouldBe("the team's own All.sln\n");
        Read(root, Program).ShouldBe("a\nb\nc\nd\ne\nteam\n");
    }

    [Test]
    public async Task ALineBothChanged_IsAConflictGitMarks_AndTheNextUpgradeStartsFromTheBranch()
    {
        var root = await SolutionAsync();
        Write(root, Program, "a\nb\nC by the team\nd\ne\nteam\n");
        await Git(root, "commit", "-q", "-am", "the team's change");
        var merge = new GitTemplateMerge(Runner);

        var conflicts = await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((Program, "a\nb\nC by the template\nd\ne\n")), "2.0.0"), false, CancellationToken.None);

        conflicts.ShouldBe([Program]);
        Read(root, Program).ShouldContain("<<<<<<<");
        await Git(root, "merge", "--abort");
        Read(root, Program).ShouldBe("a\nb\nC by the team\nd\ne\nteam\n");

        // Resolved the template's way and committed, the next version merges over 2.0.0, not over 1.0.0 again.
        await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((Program, "a\nb\nC by the template\nd\ne\n")), "2.0.0"), true, CancellationToken.None);
        await Git(root, "commit", "-q", "--no-edit");
        var again = await merge.MergeAsync(root, new(Base(), "1.0.0"), new(Tree((Program, "new in 3.0\na\nb\nC by the template\nd\ne\n")), "3.0.0"), false, CancellationToken.None);

        again.ShouldBeEmpty();
        Read(root, Program).ShouldBe("new in 3.0\na\nb\nC by the template\nd\ne\nteam\n");
    }
}
