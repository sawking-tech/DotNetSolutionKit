using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.Tests;

/// <summary>Every branch of the three-way merge, on real files and the real git merge-file.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class ThreeWayMergeTests
{
    private const string File = "src/a.cs";

    private sealed class Trees
    {
        public string Solution { get; } = Folders.NewTemp("t-solution");
        public string Base { get; } = Folders.NewTemp("t-base");
        public string New { get; } = Folders.NewTemp("t-new");

        public Trees Put(string root, string text, bool bom = false)
        {
            var path = Path.Combine(root, File);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, new FileText(text, bom).ToBytes());
            return this;
        }

        public async Task<Change?> MergeAsync(bool force = false) =>
            (await new ThreeWayMerge(new GitMergeFile(new ProcessRunner())).ComputeAsync(Solution, Base, New, force, null, CancellationToken.None)).SingleOrDefault();
    }

    /// <summary>A merger that fails as git merge-file does when it cannot merge at all.</summary>
    private sealed class FailingMerger : ITextMerger
    {
        public Task<TextMergeResult> MergeAsync(string solution, string @base, string template, bool preferTemplate, CancellationToken ct) =>
            Task.FromResult(new TextMergeResult(string.Empty, 0, "error: Cannot merge binary files"));
    }

    [Test]
    public async Task Should_RefuseTheCommand_When_GitCannotMergeAFile()
    {
        var trees = new Trees();
        trees.Put(trees.Base, "a\n").Put(trees.Solution, "a\nsolution\n").Put(trees.New, "a\ntemplate\n");

        var refused = await Should.ThrowAsync<InvalidOperationException>(() =>
            new ThreeWayMerge(new FailingMerger()).ComputeAsync(trees.Solution, trees.Base, trees.New, false, null, CancellationToken.None));

        refused.Message.ShouldContain(File);
    }

    [Test]
    public async Task Should_MergeTheTeamsLineIntoTheTemplatesStyle_When_TheTemplateChangedOnlyItsStyle()
    {
        var trees = new Trees();
        trees.Put(trees.Base, "a  \nb", bom: true).Put(trees.Solution, "a  \nb\nteam\n", bom: true).Put(trees.New, "a\nb\n");

        var change = await trees.MergeAsync();

        change.ShouldNotBeNull().Kind.ShouldBe(ChangeKind.Merge);
        change.Content.ShouldBe(new FileText("a\nb\nteam\n", false));
    }

    [Test]
    public async Task Should_MarkAConflict_InTheTemplatesStyle_When_BothChangedOneLine()
    {
        var trees = new Trees();
        trees.Put(trees.Base, "a\nb", bom: true).Put(trees.Solution, "a\nteam  \n", bom: true).Put(trees.New, "a\ntemplate\n");

        var change = await trees.MergeAsync();

        change.ShouldNotBeNull().Kind.ShouldBe(ChangeKind.Conflict);
        change.Content.ShouldNotBeNull().Bom.ShouldBeFalse();
        change.Content.Text.ShouldContain("team\n=======");
    }

    [Test]
    public async Task AMergedText_KeepsItsCharactersBeyondAscii_WhateverTheConsolesCodePage()
    {
        // A dash and Cyrillic: read by the console's code page they came back as "тАФ".
        const string line = "// a dash — and Cyrillic: проверка\n";

        var merged = await new GitMergeFile(new ProcessRunner()).MergeAsync(line + "team\n", line, "template\n" + line, false, CancellationToken.None);

        merged.Conflicts.ShouldBe(0);
        merged.Text.ShouldBe("template\n" + line + "team\n");
    }

    [Test]
    public async Task Should_Add_When_OnlyTheTemplateHasTheFile()
    {
        var trees = new Trees();
        trees.Put(trees.New, "new\n");

        var change = await trees.MergeAsync();

        change!.Kind.ShouldBe(ChangeKind.Add);
    }

    [Test]
    public async Task Should_TakeTheTemplates_When_TheSolutionDidNotChangeTheFile()
    {
        var t = new Trees();
        t.Put(t.Base, "a\n").Put(t.Solution, "a\n").Put(t.New, "b\n");

        var change = await t.MergeAsync();

        change!.Kind.ShouldBe(ChangeKind.Update);
        change.Content!.Text.ShouldBe("b\n");
    }

    [Test]
    public async Task Should_LeaveTheSolutions_When_TheTemplateDidNotChangeTheFile()
    {
        var t = new Trees();
        t.Put(t.Base, "a\n").Put(t.Solution, "mine\n").Put(t.New, "a\n");

        (await t.MergeAsync()).ShouldBeNull();
    }

    [Test]
    public async Task Should_KeepBoth_When_TheyChangedDifferentLines()
    {
        var t = new Trees();
        t.Put(t.Base, "1\n2\n3\n4\n5\n").Put(t.Solution, "mine\n2\n3\n4\n5\n").Put(t.New, "1\n2\n3\n4\ntemplate\n");

        var change = await t.MergeAsync();

        change!.Kind.ShouldBe(ChangeKind.Merge);
        change.Content!.Text.ShouldBe("mine\n2\n3\n4\ntemplate\n");
    }

    [Test]
    public async Task Should_MarkAConflict_When_TheyChangedTheSameLine()
    {
        var t = new Trees();
        t.Put(t.Base, "1\n2\n3\n").Put(t.Solution, "1\nmine\n3\n").Put(t.New, "1\ntemplate\n3\n");

        var change = await t.MergeAsync();

        change!.Kind.ShouldBe(ChangeKind.Conflict);
        change.Content!.Text.ShouldContain("<<<<<<< solution");
        change.Content.Text.ShouldContain(">>>>>>> template");
    }

    [Test]
    public async Task Should_TakeTheTemplatesLine_When_ForcedThroughAConflict()
    {
        var t = new Trees();
        t.Put(t.Base, "1\n2\n3\n").Put(t.Solution, "1\nmine\n3\n").Put(t.New, "1\ntemplate\n3\n");

        var change = await t.MergeAsync(force: true);

        change!.Content!.Text.ShouldBe("1\ntemplate\n3\n");
    }

    [Test]
    public async Task Should_Delete_When_TheTemplateRemovedAFileTheSolutionDidNotChange()
    {
        var t = new Trees();
        t.Put(t.Base, "a\n").Put(t.Solution, "a\n");

        (await t.MergeAsync())!.Kind.ShouldBe(ChangeKind.Delete);
    }

    [Test]
    public async Task Should_KeepAndReport_When_TheTemplateRemovedAFileTheSolutionChanged()
    {
        var t = new Trees();
        t.Put(t.Base, "a\n").Put(t.Solution, "mine\n");

        var change = await t.MergeAsync();

        change!.Kind.ShouldBe(ChangeKind.Kept);
        change.Content.ShouldBeNull();
    }

    [Test]
    public async Task Should_SeeNoChange_When_TheSolutionDiffersInLineEndingsOnly()
    {
        var t = new Trees();
        t.Put(t.Base, "a\nb\n").Put(t.Solution, "a\r\nb\r\n").Put(t.New, "a\nb\n");

        (await t.MergeAsync()).ShouldBeNull();
    }

    [Test]
    public async Task Should_WriteTheSolutionsLineEndingsAndMark_When_ItTakesTheTemplates()
    {
        var t = new Trees();
        t.Put(t.Base, "a\n", bom: true).Put(t.Solution, "a\r\n", bom: true).Put(t.New, "b\n", bom: true);

        var change = await t.MergeAsync();

        change!.Content!.Text.ShouldBe("b\r\n");
        change.Content.ToBytes().Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
    }
}
