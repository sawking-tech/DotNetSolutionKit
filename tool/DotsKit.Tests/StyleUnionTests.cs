using DotsKit.Merging;

namespace DotsKit.Tests;

/// <summary>The union of the template's style and the team's, with no formatter and no files.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class StyleUnionTests
{
    private const string Template = "root = true\n\n[*.cs]\nindent_size = 4\ncsharp_prefer_braces = true\n";

    [Test]
    public void Should_KeepTheTemplateRules_When_TheTeamHasNoFile() =>
        StyleUnion.Of(Template, null).ShouldBe(Template);

    [Test]
    public void Should_PutTheTeamsRules_AfterTheTemplates() =>
        StyleUnion.Of(Template, "root = true\n[*.cs]\nindent_size = 2\n")
            .ShouldBe("root = true\n\n[*.cs]\nindent_size = 4\ncsharp_prefer_braces = true\n\n[*.cs]\nindent_size = 2\n");

    [Test]
    public void Should_StateTheRootOnce_When_TheTeamStatesItToo() =>
        StyleUnion.Of(Template, "root = true\n[*.cs]\nindent_size = 2\n").Split("root").Length.ShouldBe(2);

    [Test]
    public void Should_KeepTheTeamsComments() =>
        StyleUnion.Of(Template, "# our style\n[*.cs]\nindent_size = 2\n").ShouldContain("# our style");

    [Test]
    public void Should_EndTheFile_WithOneLineEnd() =>
        StyleUnion.Of(Template, "[*.cs]\nindent_size = 2").ShouldEndWith("indent_size = 2\n");

    [Test]
    public void Should_ReadTheTeamsFileWithCrLfAsLf() =>
        StyleUnion.Of(Template, "root = true\r\n[*.cs]\r\nindent_size = 2\r\n").ShouldNotContain("\r");

    [Test]
    public void Should_GiveTheTemplateAlone_When_TheTeamFileIsBlank() =>
        StyleUnion.Of(Template, "  \n\n").ShouldBe(Template.Replace("\r", ""));
}
