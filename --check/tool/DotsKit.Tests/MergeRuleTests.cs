using DotsKit.Merging;

namespace DotsKit.Tests;

/// <summary>The decision for a file from its three versions, without files or git.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class MergeRuleTests
{
    private const string Path = "src/a.cs";

    private static FileText T(string text) => new(text, Bom: false);

    private static Change? Final(MergeRule.Decision decision) => decision.ShouldBeOfType<MergeRule.Final>().Change;

    [Test]
    public void Should_DoNothing_When_TheSolutionAlreadyHasTheTemplatesText() =>
        Final(MergeRule.Decide(Path, T("a\n"), T("b\n"), T("b\n"))).ShouldBeNull();

    [Test]
    public void Should_Add_When_OnlyTheTemplateHasTheFile() =>
        Final(MergeRule.Decide(Path, null, T("a\n"), null))!.Kind.ShouldBe(ChangeKind.Add);

    [Test]
    public void Should_MergeWithAnEmptyBase_When_BothAddedTheFile()
    {
        var merge = MergeRule.Decide(Path, null, T("template\n"), T("mine\n")).ShouldBeOfType<MergeRule.NeedsMerge>();

        merge.Base.Text.ShouldBeEmpty();
    }

    [Test]
    public void Should_LeaveTheSolutions_When_TheTemplateDidNotChangeIt() =>
        Final(MergeRule.Decide(Path, T("a\n"), T("a\n"), T("mine\n"))).ShouldBeNull();

    [Test]
    public void Should_TakeTheTemplates_When_TheSolutionDidNotChangeIt() =>
        Final(MergeRule.Decide(Path, T("a\n"), T("b\n"), T("a\n")))!.Kind.ShouldBe(ChangeKind.Update);

    [Test]
    public void Should_NeedAMerge_When_BothChangedIt() =>
        MergeRule.Decide(Path, T("a\n"), T("b\n"), T("c\n")).ShouldBeOfType<MergeRule.NeedsMerge>();

    [Test]
    public void Should_Delete_When_TheTemplateRemovedAFileTheSolutionDidNotChange() =>
        Final(MergeRule.Decide(Path, T("a\n"), null, T("a\n")))!.Kind.ShouldBe(ChangeKind.Delete);

    [Test]
    public void Should_KeepAndReport_When_TheTemplateRemovedAFileTheSolutionChanged()
    {
        var change = Final(MergeRule.Decide(Path, T("a\n"), null, T("mine\n")))!;

        change.Kind.ShouldBe(ChangeKind.Kept);
        change.Content.ShouldBeNull();
    }

    [Test]
    public void Should_LeaveItRemovedAndReport_When_TheSolutionRemovedAFileTheTemplateChanged()
    {
        var change = Final(MergeRule.Decide(Path, T("a\n"), T("b\n"), null))!;

        change.Kind.ShouldBe(ChangeKind.Kept);
        change.Content.ShouldBeNull();
    }

    [Test]
    public void Should_SeeNoChange_When_OnlyTheLineEndingsDiffer() =>
        Final(MergeRule.Decide(Path, T("a\nb\n"), T("a\nb\n"), T("a\r\nb\r\n"))).ShouldBeNull();

    [Test]
    public void Should_KeepTheSolutionsLineEndings_When_ItTakesTheTemplates() =>
        Final(MergeRule.Decide(Path, T("a\r\n"), T("b\n"), T("a\r\n")))!.Content!.Text.ShouldBe("b\r\n");
}
