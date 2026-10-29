using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.Tests;

/// <summary>The parts of the merge by git that decide: what the template changes between two outputs, and the option.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class GitModeTests
{
    private const string File = "src/a.cs";

    private static FileText Text(string text) => new(text, Bom: false);

    [Test]
    public void TheTemplatesChange_IsAFileAddedRemovedOrChanged_AndAFileItKeptIsNone()
    {
        TemplateDiff.Decide(File, null, Text("a\n"))!.Kind.ShouldBe(ChangeKind.Add);
        TemplateDiff.Decide(File, Text("a\n"), null)!.Kind.ShouldBe(ChangeKind.Delete);
        TemplateDiff.Decide(File, Text("a\n"), Text("b\n"))!.Kind.ShouldBe(ChangeKind.Update);
        TemplateDiff.Decide(File, Text("a\n"), Text("a\r\n")).ShouldBeNull();
    }

    [Test]
    public void ASolutionThereAlready_IsMergedByGit_UnlessMergeFilesIsNamed()
    {
        Options.Parse("upgrade", ["--yes"]).ByGit.ShouldBeTrue();
        Options.Parse("upgrade", ["--merge", "files"]).ByGit.ShouldBeFalse();
        Options.Parse("new", ["-S", "Billing", "--merge", "git"]).ByGit.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => Options.Parse("upgrade", ["--merge", "svn"])).Message.ShouldContain("name git or files");
        Should.Throw<ArgumentException>(() => Options.Parse("init", ["--merge", "files"])).Message.ShouldContain("init merges nothing");
    }

    [Test]
    public async Task TheFilesTheSolutionsGitIgnores_AreNamed_AndNoneOutsideGit()
    {
        var runner = new ProcessRunner();
        var root = Folders.NewTemp("ignored");
        Directory.CreateDirectory(Path.Combine(root, "src", "Api"));
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "**/appsettings.Secrets.json\n");
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "src", "Api", "appsettings.Secrets.json"), "{}\n");
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "src", "Api", "appsettings.json"), "{}\n");
        var outsideGit = await new GitIgnoredFiles(runner).InAsync(root, CancellationToken.None);
        await runner.RunCheckedAsync("git", ["init", "-q"], root, CancellationToken.None);

        var ignored = await new GitIgnoredFiles(runner).InAsync(root, CancellationToken.None);

        ignored.ShouldBe(["src/Api/appsettings.Secrets.json"]);
        outsideGit.ShouldBeEmpty();
    }
}
