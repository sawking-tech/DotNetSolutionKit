using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.Tests;

/// <summary>The template's style the merges bring a file to, and how git is given the driver that does it.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class TemplateStyleTests
{
    [Test]
    public void AText_LosesItsByteOrderMarkAndTheSpacesAtTheEndsOfLines_AndEndsWithALineEnd_KeepingItsLineEnds()
    {
        TemplateStyle.Of("a.cs", new FileText("a  \nb", Bom: true)).ShouldBe(new FileText("a\nb\n", false));
        TemplateStyle.Of("a.cs", new FileText("a\t\r\nb\r\n\r\n", Bom: false)).ShouldBe(new FileText("a\r\nb\r\n\r\n", false));
        TemplateStyle.Of("a.cs", new FileText("", Bom: true)).ShouldBe(new FileText("", false));
    }

    [Test]
    public void ASolutionFile_AndABinaryFile_StayAsTheyAre()
    {
        var solution = new FileText("Project  \n", Bom: true);
        var binary = new FileText(string.Empty, false, [0, 1, 32, 32]);

        TemplateStyle.Of("src/services/Acme.Shop.All.sln", solution).ShouldBeSameAs(solution);
        TemplateStyle.Of("a.png", binary).ShouldBeSameAs(binary);
    }

    [Test]
    public void TheToolRunsItselfAgain_ByItsAppHost_OrByDotnetWithItsAssembly_Quoted()
    {
        SelfCommand.Of(@"C:\Users\A B\.dotnet\tools\dotskit.exe", "x.dll").ShouldBe("\"C:/Users/A B/.dotnet/tools/dotskit.exe\"");
        SelfCommand.Of("/usr/share/dotnet/dotnet", "/t/dotskit.dll").ShouldBe("\"/usr/share/dotnet/dotnet\" \"/t/dotskit.dll\"");
        SelfCommand.Of(null, "/t/dotskit.dll").ShouldBe("\"dotnet\" \"/t/dotskit.dll\"");
    }

    [Test]
    public async Task TheDriver_RunByHand_SaysItIsGitsInOneLine()
    {
        var refused = await Should.ThrowAsync<ArgumentException>(() =>
            new StyleMergeDriver(new GitMergeFile(new ProcessRunner())).RunAsync(["a"], CancellationToken.None));

        refused.Message.ShouldContain("it is not a command to run by hand");
    }

    [Test]
    public async Task TheDriver_ThatCannotMerge_SaysHowToTakeTheTemplatesSide()
    {
        var folder = Folders.NewTemp("driver");
        string[] sides = ["base", "solution", "template"];
        foreach (var side in sides)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, side), "a\n");
        }

        var failed = await Should.ThrowAsync<InvalidOperationException>(() => new StyleMergeDriver(new FailingMerger())
            .RunAsync([.. sides.Select(side => Path.Combine(folder, side)), "src/a.cs"], CancellationToken.None));

        failed.Message.ShouldContain("git could not merge src/a.cs: no merge");
        failed.Message.ShouldContain("git checkout --theirs -- src/a.cs");
    }

    private sealed class FailingMerger : ITextMerger
    {
        public Task<TextMergeResult> MergeAsync(string solution, string @base, string template, bool preferTemplate, CancellationToken ct) =>
            Task.FromResult(new TextMergeResult(string.Empty, 0, "no merge"));
    }
}
