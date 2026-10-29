using DotsKit.Io;
using DotsKit.Merging;
using static DotsKit.Tests.Scenarios.GitMerge.GitSolution;

namespace DotsKit.Tests.Scenarios.GitMerge;

/// <summary>
/// The merge by git brings the three sides of a file to the template's style first: a change of style alone is no
/// conflict, a solution file keeps its byte order mark, and the merged file keeps the team's line ends.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class StyleScenarios
{
    [Test]
    public async Task TheTemplatesStyleAlone_ConflictsWithNothingTheTeamWrote_AndTheMergedFileHasTheStyle()
    {
        var root = Folders.NewTemp("git-merge-style");
        WriteWithBom(root, ProgramFile, "a  \nb\nteam\n");
        await Git(root, "init", "-q");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the solution");
        var @base = Folders.NewTemp("git-merge-tree");
        WriteWithBom(@base, ProgramFile, "a  \nb");

        var conflicts = await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(@base, "2.8.0"), new(Tree((ProgramFile, "a\nb\n")), "2.9.0"), false,
            CancellationToken.None);

        conflicts.ShouldBeEmpty();
        Read(root, ProgramFile).ShouldBe("a\nb\nteam\n");
        File.ReadAllBytes(Path.Combine(root, ProgramFile)).AsSpan().StartsWith(Bom).ShouldBeFalse();
    }

    [Test]
    public async Task ASolutionFileBothChanged_KeepsItsByteOrderMark_EvenWithASpaceInItsPath()
    {
        const string solutionFile = "src/services/Acme Shop/Orders.sln";
        var root = Folders.NewTemp("git-merge-sln");
        WriteWithBom(root, solutionFile, "a\nb\nteam\n");
        await Git(root, "init", "-q");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the solution");
        var (@base, next) = (Folders.NewTemp("git-merge-tree"), Folders.NewTemp("git-merge-tree"));
        WriteWithBom(@base, solutionFile, "a\nb\n");
        WriteWithBom(next, solutionFile, "template\na\nb\n");

        var conflicts = await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(@base, "2.8.0"), new(next, "2.9.0"), false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        Read(root, solutionFile).ShouldBe("template\na\nb\nteam\n");
        File.ReadAllBytes(Path.Combine(root, solutionFile)).AsSpan().StartsWith(Bom).ShouldBeTrue();
    }

    [Test]
    public async Task AFileTheTeamKeepsInCrLf_IsMergedInCrLf()
    {
        var root = Folders.NewTemp("git-merge-crlf");
        await Git(root, "init", "-q");
        await Git(root, "config", "core.autocrlf", "false");
        Write(root, ProgramFile, "a\r\nb\r\nteam\r\n");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the solution");

        var conflicts = await new GitTemplateMerge(Runner, Sandbox.Self).MergeAsync(root, new(Tree((ProgramFile, "a  \nb\n")), "2.8.0"),
            new(Tree((ProgramFile, "template\na\nb\n")), "2.9.0"), false, CancellationToken.None);

        conflicts.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(root, ProgramFile)).ShouldBe("template\r\na\r\nb\r\nteam\r\n");
    }
}
