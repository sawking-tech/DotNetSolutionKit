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

    /// <summary>
    /// Every line of the team's file goes in as written. A severity is not raised: the formatter that applies the union
    /// reads the option's value, and a raised severity on an analyzer rule makes the formatter rewrite the code.
    /// </summary>
    [Test]
    public void Should_ChangeNoSeverity_TheTeamWrites() =>
        StyleUnion.Of(Template, "dotnet_diagnostic.IDE0011.severity = suggestion\ncsharp_prefer_braces = true:suggestion\n")
            .ShouldContain("dotnet_diagnostic.IDE0011.severity = suggestion\ncsharp_prefer_braces = true:suggestion");

    /// <summary>
    /// An option that takes a list of values - `all`, `before_and_after` - accepts no `:severity` suffix: appending one
    /// gives a value the formatter cannot read, and the invalid line, being later, overrides the template's valid one.
    /// </summary>
    [Test]
    public void Should_NotAppendASeverity_ToAValueWithoutOne()
    {
        var union = StyleUnion.Of(Template, "csharp_new_line_before_open_brace = none\n");

        union.ShouldContain("csharp_new_line_before_open_brace = none\n");
        union.ShouldNotContain("none:warning");
    }

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
