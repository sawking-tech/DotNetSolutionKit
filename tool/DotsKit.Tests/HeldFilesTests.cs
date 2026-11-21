using DotsKit.Ignoring;
using DotsKit.Io;

namespace DotsKit.Tests;

/// <summary>
/// What a rule of <c>.dotskitignore</c> does to the template's output: a held path takes the base's text, so the merge
/// by git finds nothing of the template there and the team's file stands. No git: the trees, on real files.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class HeldFilesTests
{
    private static string Tree(params (string Path, string Text)[] files)
    {
        var root = Folders.NewTemp("held");
        foreach (var (path, text) in files)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        return root;
    }

    [Test]
    public void AHeldFile_TheTemplateChanged_TakesTheTextOfTheBase_SoTheTeamKeepsItsOwn()
    {
        var @base = Tree(("src/common/Keep.cs", "the team's version"));
        var next = Tree(("src/common/Keep.cs", "the template's new version"), ("src/common/Free.cs", "new"));

        HeldFiles.Apply(next, @base, new HashSet<string> { "src/common/Keep.cs" });

        File.ReadAllText(Path.Combine(next, "src/common/Keep.cs")).ShouldBe("the team's version");
        File.ReadAllText(Path.Combine(next, "src/common/Free.cs")).ShouldBe("new");
    }

    [Test]
    public void AHeldFile_TheTemplateAdds_LeavesTheOutput_AsTheTemplateWouldTouchIt()
    {
        var @base = Tree(("src/common/There.cs", "x"));
        var next = Tree(("src/common/There.cs", "x"), ("src/common/Added.cs", "new"));

        HeldFiles.Apply(next, @base, new HashSet<string> { "src/common/Added.cs" });

        File.Exists(Path.Combine(next, "src/common/Added.cs")).ShouldBeFalse();
        File.Exists(Path.Combine(next, "src/common/There.cs")).ShouldBeTrue();
    }

    [Test]
    public void AHeldFile_TheTemplateRemoves_ComesBackFromTheBase()
    {
        var @base = Tree(("src/common/Gone.cs", "kept by the team"));
        var next = Tree([]);

        HeldFiles.Apply(next, @base, new HashSet<string> { "src/common/Gone.cs" });

        File.ReadAllText(Path.Combine(next, "src/common/Gone.cs")).ShouldBe("kept by the team");
    }

    [Test]
    public void NoBaseAndNoRule_LeaveTheOutputAsTheTemplateWroteIt()
    {
        var next = Tree(("src/common/Keep.cs", "the template's"));

        HeldFiles.Apply(next, null, new HashSet<string> { "src/common/Keep.cs" });
        HeldFiles.Apply(next, Folders.NewTemp("empty"), new HashSet<string>());

        File.ReadAllText(Path.Combine(next, "src/common/Keep.cs")).ShouldBe("the template's");
    }
}
