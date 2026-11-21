using DotsKit.Ignoring;
using DotsKit.Io;

namespace DotsKit.Tests;

/// <summary>
/// The matching of <c>.dotskitignore</c>, done by git itself: a path of the solution and a pattern are compared by the
/// syntax git reads, so the tool and the team read the file the same way. On the real git, since git is what the tool
/// asks.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class IgnoreRulesTests
{
    private static string Tree(params string[] files)
    {
        var root = Folders.NewTemp("ignore-tree");
        foreach (var file in files)
        {
            var full = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "");
        }

        return root;
    }

    private static async Task<IReadOnlySet<string>> Held(string rules, string[] tree, string to = "3.0.0")
    {
        var root = Tree(tree);
        File.WriteAllText(IgnoreRules.PathOf(root), rules);
        var patterns = new GitIgnorePatterns(new ProcessRunner());
        return (await new IgnoreRules(patterns).DecideAsync(root, to, "2.9.0", tree, CancellationToken.None)).Held;
    }

    [Test]
    public async Task APathTheFileNames_IsHeld_AndNoOtherIs()
    {
        var held = await Held("src/common/ST.Demo.Common.Infrastructure/Locked.cs\n", [
            "src/common/ST.Demo.Common.Infrastructure/Locked.cs",
            "src/common/ST.Demo.Common.Infrastructure/Free.cs",
        ]);

        held.ShouldBe(["src/common/ST.Demo.Common.Infrastructure/Locked.cs"]);
    }

    [Test]
    public async Task ACancellingRule_KeepsTheOneFileBack_AsGitignoreHolds()
    {
        // The rules of the file are git's own, its negation included: a pattern that names files lets a ! bring one
        // back; a pattern that names a folder excludes what is in it, and git re-enters no excluded folder.
        var held = await Held("src/capabilities/**/*.cs\n!src/capabilities/ST.Demo.Capabilities.Messaging.RabbitMQ/Keep.cs\n", [
            "src/capabilities/ST.Demo.Capabilities.Messaging.RabbitMQ/Keep.cs",
            "src/capabilities/ST.Demo.Capabilities.Messaging.RabbitMQ/Gone.cs",
        ]);

        held.ShouldBe(["src/capabilities/ST.Demo.Capabilities.Messaging.RabbitMQ/Gone.cs"]);
    }

    [Test]
    public async Task AnExpiredPin_LetsItsPathsThrough_AndNamesTheLineTheReviewShows()
    {
        var root = Tree(["src/common/X/Taken.cs"]);
        File.WriteAllText(IgnoreRules.PathOf(root), "# ours\nsrc/common/X/@2.9\n");

        var decision = await new IgnoreRules(new GitIgnorePatterns(new ProcessRunner()))
            .DecideAsync(root, "3.0.0", "2.9.0", ["src/common/X/Taken.cs"], CancellationToken.None);

        decision.Held.ShouldBeEmpty();
        decision.Spent.ShouldHaveSingleItem().Line.ShouldBe(2);
        decision.Spent[0].Pattern.ShouldBe("src/common/X/");
    }

    [Test]
    public async Task ACancellationRule_BringsItsFileBack_AndAPathBroughtBackIsNoHold()
    {
        // git names a ! rule for the path it brings back; a path brought back by the team's own rule is the template's
        // to write, so it is never held, whatever the pin of the rule that named it.
        var root = Tree(["src/x/A.cs", "src/x/Keep.cs"]);
        File.WriteAllText(IgnoreRules.PathOf(root), "src/x/*.cs\n!src/x/Keep.cs\n");

        var decision = await new IgnoreRules(new GitIgnorePatterns(new ProcessRunner()))
            .DecideAsync(root, "3.0.0", "2.9.0", ["src/x/A.cs", "src/x/Keep.cs"], CancellationToken.None);

        decision.Held.ShouldBe(["src/x/A.cs"]);
    }

    [Test]
    public async Task ASpentRule_StillNamesThePathsItsReviewAsksAbout()
    {
        var root = Tree(["src/x/A.cs"]);
        File.WriteAllText(IgnoreRules.PathOf(root), "src/x/@2.9\n");

        var decision = await new IgnoreRules(new GitIgnorePatterns(new ProcessRunner()))
            .DecideAsync(root, "3.0.0", "2.9.0", ["src/x/A.cs"], CancellationToken.None);

        decision.Held.ShouldBeEmpty();
        decision.PathsOf[decision.Spent[0]].ShouldBe(new HashSet<string> { "src/x/A.cs" }, customMessage: "the review reads these paths against the template's own");
    }

    [Test]
    public async Task AnUnexpiredPin_HoldsItsPaths()
    {
        var root = Tree(["src/common/X/Taken.cs"]);
        File.WriteAllText(IgnoreRules.PathOf(root), "src/common/X/@2.9\n");

        var decision = await new IgnoreRules(new GitIgnorePatterns(new ProcessRunner()))
            .DecideAsync(root, "2.9.4", "2.9.0", ["src/common/X/Taken.cs"], CancellationToken.None);

        decision.Held.ShouldBe(["src/common/X/Taken.cs"]);
        decision.Spent.ShouldBeEmpty();
    }

    [Test]
    public async Task ACandidateTheTemplateAdds_IsHeldToo_ThoughItIsNotOnDiskYet()
    {
        var root = Tree([]);
        File.WriteAllText(IgnoreRules.PathOf(root), "src/common/New/\n");

        var decision = await new IgnoreRules(new GitIgnorePatterns(new ProcessRunner()))
            .DecideAsync(root, "3.0.0", "2.9.0", ["src/common/New/Locked.cs"], CancellationToken.None);

        decision.Held.ShouldBe(["src/common/New/Locked.cs"]);
    }

    [Test]
    public async Task NoFile_HoldsNothing()
    {
        (await Held("", ["src/a.cs"])).ShouldBeEmpty();
    }
}
