using DotsKit.Generation;
using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Tests.Scenarios.GitMerge;

/// <summary>
/// A new solution beside files: the folder must be a git repository with the files committed by the team -
/// the tool never commits for it - and the merge is then git's own: a file that differs comes out a conflict
/// the IDE opens, and git merge --abort puts the folder back. An empty folder takes the plain write, no git.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class NewSolutionScenarios
{
    private static Manifest Next() => new()
    {
        Template = "2.8.0",
        Solution = ["--NamespaceRoot", "ST", "--ProductName", "Demo"],
        Services = [["--ServiceNameOrCustom", "Orders", "--HttpPort", "5000"]],
    };

    private static Planner Planner() =>
        new(new FixedOutput(), new SolutionProjects(Runner), new GitRenames(Runner), new GitIgnoredFiles(Runner),
            new ReferenceRepair(new RepairWriter(), new DotnetBuilder(Runner)));

    [Test]
    public async Task AnEmptyFolder_TakesThePlainWrite_WithoutGit()
    {
        var root = Folders.NewTemp("new-solution");

        var plan = await Planner().BuildAsync(root, null, Next(), new MergeMode(false, false), CancellationToken.None);

        plan.ByGit.ShouldBeNull();
        plan.Changes.ShouldAllBe(c => c.Kind == ChangeKind.Add);
    }

    [Test]
    public async Task ARepositoryWithCommittedFiles_MergesTheNewSolution_ByGit_AndAbortPutsTheFilesBack()
    {
        var root = Folders.NewTemp("new-solution");
        Write(root, "notes.md", "the team's notes\n");
        await Git(root, "init", "-q");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the team's files");

        var plan = await Planner().BuildAsync(root, null, Next(), new MergeMode(false, false), CancellationToken.None);

        if (plan.ByGit is not { } byGit)
        {
            throw new InvalidOperationException("beside files, a new solution merges by git");
        }

        var conflicts = await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, byGit.Base, byGit.Next, false, false, CancellationToken.None);

        conflicts.ShouldBe(["notes.md"]);
        Read(root, "notes.md").ShouldContain("<<<<<<<");
        await Git(root, "merge", "--abort");
        Read(root, "notes.md").ShouldBe("the team's notes\n");
    }

    [Test]
    public async Task ARepositoryWithoutGitIdentity_MergesWithMechanicalCommitsAuthoredByDotskit()
    {
        var isolated = new WithoutGitIdentity(Runner);
        var root = Folders.NewTemp("new-solution");
        Write(root, "notes.md", "the team's notes\n");
        await Git(root, "init", "-q");
        await Git(root, "add", "-A");
        await isolated.RunCheckedAsync("git", ["-c", "user.name=team", "-c", "user.email=team@example.com", "commit", "-q", "-m", "the team's files"], root, CancellationToken.None);

        var plan = await Planner().BuildAsync(root, null, Next(), new MergeMode(false, false), CancellationToken.None);

        if (plan.ByGit is not { } byGit)
        {
            throw new InvalidOperationException("beside files, a new solution merges by git");
        }

        await new GitTemplateMerge(isolated, Sandbox.Self).MergeAsync(root, byGit.Base, byGit.Next, false, false, CancellationToken.None);

        // The adopt commit on HEAD and the template's base commit are mechanical, made where git knows no
        // committer, as on a fresh runner. The merge itself stays uncommitted, as after any run.
        (await isolated.RunCheckedAsync("git", ["log", "-1", "--format=%an"], root, CancellationToken.None)).Trim()
            .ShouldBe(Io.Git.FallbackUserName);
        (await isolated.RunCheckedAsync("git", ["log", "-1", "--format=%an", "dotskit/template"], root, CancellationToken.None)).Trim()
            .ShouldBe(Io.Git.FallbackUserName);
    }

    /// <summary>The template's output for a new solution: a file of its own and one the team's copy of differs from.</summary>
    private sealed class FixedOutput : IStaging
    {
        public Task<string> BuildAsync(Manifest manifest, bool withSolutionFile, CancellationToken ct)
        {
            var root = Folders.NewTemp("new-solution-output");
            Write(root, "notes.md", "the template's notes\n");
            Write(root, "src/services/ST.Demo.Orders/ST.Demo.Orders.csproj", "<Project />\n");
            return Task.FromResult(root);
        }
    }

    private static readonly ProcessRunner Runner = new();

    private static void Write(string root, string path, string text)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static async Task Git(string root, params string[] args) => await Runner.RunCheckedAsync("git", args, root, CancellationToken.None);
}

