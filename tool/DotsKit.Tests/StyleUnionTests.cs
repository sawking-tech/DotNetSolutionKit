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

    /// <summary>The team's file is offered at suggestion by the flag; the merge applies what it names.</summary>
    [Test]
    public void Should_RaiseTheTeamsSuggestion_ToTheSeverityAFormatterApplies() =>
        StyleUnion.Of(Template, "dotnet_diagnostic.IDE0011.severity = suggestion\ncsharp_prefer_braces = true:suggestion\n")
            .ShouldContain("dotnet_diagnostic.IDE0011.severity = warning\ncsharp_prefer_braces = true:warning\n");

    /// <summary>A rule the team turned off is a rule they do not want; the merge leaves it off.</summary>
    [Test]
    public void Should_KeepTheTeamsNone_AsItIs() =>
        StyleUnion.Of(Template, "dotnet_diagnostic.IDE0011.severity = none\n").ShouldContain("dotnet_diagnostic.IDE0011.severity = none");

    /// <summary>A naming rule's severity is a severity too, and the merge raises it like any other.</summary>
    [Test]
    public void Should_RaiseEveryKindOfSeverity_TheTeamNames() =>
        StyleUnion.Of(Template, "dotnet_naming_rule.x.severity = suggestion\n").ShouldContain("dotnet_naming_rule.x.severity = warning");

    /// <summary>A rule with no severity at all is a value, and the merge leaves it as the team wrote it.</summary>
    [Test]
    public void Should_LeaveARuleWithoutASeverity_AsItIs() =>
        StyleUnion.Of(Template, "indent_size = 2\n").ShouldContain("indent_size = 2\n");

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
