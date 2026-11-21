using DotsKit.Ignoring;
using DotsKit.Io;

namespace DotsKit.Tests;

/// <summary>
/// The matching of <c>.dotskitignore</c>, done by git itself: a path of the solution and a rule are compared by the
/// syntax git reads, so the tool and the team read the files the same way. On the real git, since git is what the tool
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
        File.WriteAllText(DotskitIgnores.PathOf(root), rules);
        var patterns = new GitIgnorePatterns(new ProcessRunner());
        return (await new IgnoreRules(patterns).DecideAsync(root, to, "2.9.0", tree, CancellationToken.None)).Held;
    }

    private static async Task<IgnoreDecision> Decision(string rules, string[] tree, string to = "3.0.0") =>
        await Decision(root: Tree(tree), rules, tree, to);

    private static async Task<IgnoreDecision> Decision(string root, string rules, string[] tree, string to = "3.0.0")
    {
        File.WriteAllText(DotskitIgnores.PathOf(root), rules);
        var patterns = new GitIgnorePatterns(new ProcessRunner());
        return await new IgnoreRules(patterns).DecideAsync(root, to, "2.9.0", tree, CancellationToken.None);
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
        var decision = await Decision("# ours\nsrc/common/X/@2.9\n", ["src/common/X/Taken.cs"]);

        decision.Held.ShouldBeEmpty();
        decision.Spent.ShouldHaveSingleItem().Line.ShouldBe(2);
        decision.Spent[0].Pattern.ShouldBe("src/common/X/");
    }

    [Test]
    public async Task ACancellationRule_BringsItsFileBack_AndAPathBroughtBackIsNoHold()
    {
        // git names a ! rule for the path it brings back; a path brought back by the team's own rule is the template's
        // to write, so it is never held, whatever the pin of the rule that named it.
        var decision = await Decision("src/x/*.cs\n!src/x/Keep.cs\n", ["src/x/A.cs", "src/x/Keep.cs"]);

        decision.Held.ShouldBe(["src/x/A.cs"]);
    }

    [Test]
    public async Task ASpentRule_StillNamesThePathsItsReviewAsksAbout()
    {
        var decision = await Decision("src/x/@2.9\n", ["src/x/A.cs"]);

        decision.Held.ShouldBeEmpty();
        decision.PathsOf[decision.Spent[0]].ShouldBe(new HashSet<string> { "src/x/A.cs" }, customMessage: "the review reads these paths against the template's own");
    }

    [Test]
    public async Task AnUnexpiredPin_HoldsItsPaths()
    {
        var decision = await Decision("src/common/X/@2.9\n", ["src/common/X/Taken.cs"], to: "2.9.4");

        decision.Held.ShouldBe(["src/common/X/Taken.cs"]);
        decision.Spent.ShouldBeEmpty();
    }

    [Test]
    public async Task ACandidateTheTemplateAdds_IsHeldToo_ThoughItIsNotOnDiskYet()
    {
        var root = Tree([]);
        var decision = await Decision(root, "src/common/New/\n", ["src/common/New/Locked.cs"]);

        decision.Held.ShouldBe(["src/common/New/Locked.cs"]);
    }

    [Test]
    public async Task NoFile_HoldsNothing()
    {
        (await Held("", ["src/a.cs"])).ShouldBeEmpty();
    }

    /// <summary>
    /// git numbers the lines of the file the team edits, a comment and a blank among them: the file goes to git as it
    /// stands, so a heading above the rules shifts no rule and the review names the line a person reads.
    /// </summary>
    [Test]
    public async Task AHeadingAboveTheRules_ShiftsNoRule_AndThePathIsHeldAllTheSame()
    {
        var withHeading = await Held(
            "# Our files, kept as we have them\n\n# the infrastructure stays ours\nsrc/common/X/Locked.cs\n",
            ["src/common/X/Locked.cs", "src/common/X/Free.cs"]);
        var withoutHeading = await Held("src/common/X/Locked.cs\n", ["src/common/X/Locked.cs", "src/common/X/Free.cs"]);

        withHeading.ShouldBe(["src/common/X/Locked.cs"]);
        withHeading.ShouldBe(withoutHeading);
    }

    [Test]
    public async Task ARuleUnderAHeading_SpendsItsPin_AndTheReviewNamesItsOwnLine()
    {
        var decision = await Decision("# ours\n\nsrc/common/X/@2.9\n", ["src/common/X/Taken.cs"]);

        decision.Held.ShouldBeEmpty();
        decision.Spent.ShouldHaveSingleItem().Line.ShouldBe(3);
        decision.PathsOf[decision.Spent[0]].ShouldBe(new HashSet<string> { "src/common/X/Taken.cs" });
    }

    /// <summary>
    /// ADR-009: a file in any directory holds its own subtree. A rule of <c>src/x/.dotskitignore</c> names the paths
    /// under <c>src/x</c>, in the words of that folder, and never reaches out of it.
    /// </summary>
    [Test]
    public async Task ARuleOfADeeperFile_HoldsTheSubtreeOfItsFolder_AndNeverOutside()
    {
        var root = Tree(["src/x/A.cs", "src/y/B.cs", "src/x/deep/C.cs", "src/other/D.cs"]);
        Directory.CreateDirectory(Path.Combine(root, "src/x"));
        File.WriteAllText(DotskitIgnores.PathOf(root, "src/x"), "*.cs\n");

        var decision = await new IgnoreRules(new GitIgnorePatterns(new ProcessRunner())).DecideAsync(
            root, "3.0.0", "2.9.0", ["src/x/A.cs", "src/x/deep/C.cs", "src/y/B.cs", "src/other/D.cs"], CancellationToken.None);

        decision.Held.Order(StringComparer.Ordinal).ToList().ShouldBe(["src/x/A.cs", "src/x/deep/C.cs"], customMessage: "the rule acts on the subtree of its own folder, and git's own syntax reads *.cs as the files of any depth below");
    }

    /// <summary>A deeper file overrides a higher one, as the ADR takes from .gitignore.</summary>
    [Test]
    public async Task ADeeperFile_OverridesTheHigherOne()
    {
        var root = Tree(["src/x/Keep.cs", "src/x/Gone.cs", "src/y/Gone.cs"]);
        Directory.CreateDirectory(Path.Combine(root, "src/x"));
        File.WriteAllText(DotskitIgnores.PathOf(root), "src/**/*.cs\n");
        File.WriteAllText(DotskitIgnores.PathOf(root, "src/x"), "!Keep.cs\n");

        var decision = await new IgnoreRules(new GitIgnorePatterns(new ProcessRunner())).DecideAsync(
            root, "3.0.0", "2.9.0", ["src/x/Keep.cs", "src/x/Gone.cs", "src/y/Gone.cs"], CancellationToken.None);

        decision.Held.ShouldBe(["src/x/Gone.cs", "src/y/Gone.cs"]);
    }

    /// <summary>A file of a deeper folder spends its own pin, and the rule the review names carries its folder.</summary>
    [Test]
    public async Task APinOfADeeperFile_IsSpentAndNamedWithItsFile()
    {
        var root = Tree(["src/x/A.cs"]);
        Directory.CreateDirectory(Path.Combine(root, "src/x"));
        File.WriteAllText(DotskitIgnores.PathOf(root, "src/x"), "a.cs@2.9\n");

        var decision = await new IgnoreRules(new GitIgnorePatterns(new ProcessRunner())).DecideAsync(
            root, "3.0.0", "2.9.0", ["src/x/A.cs"], CancellationToken.None);

        decision.Held.ShouldBeEmpty();
        decision.Spent.ShouldHaveSingleItem().FilePath.ShouldBe("src/x/.dotskitignore");
        decision.Spent[0].Line.ShouldBe(1);
    }
}
