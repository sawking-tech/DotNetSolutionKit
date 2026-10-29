using DotsKit.Commands;
using DotsKit.Generation;
using DotsKit.Io;
using DotsKit.Merging;
using DotsKit.PackageUpdates;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>
/// What an upgrade adds to the merge of dotskit new: the package policy, the files the template moved, the build's
/// errors by project. On real files and the real git where they touch them.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class UpgradePartsTests
{
    private const string Central = "src/Directory.Packages.props";

    private static string Props(params (string Name, string Version)[] packages) =>
        "<Project>\n  <ItemGroup>\n"
        + string.Concat(packages.Select(p => $"    <PackageVersion Include=\"{p.Name}\" Version=\"{p.Version}\" />\n"))
        + "  </ItemGroup>\n</Project>\n";

    private static void Put(string root, string path, string text)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    [TestCase(PackagePolicy.Minor, "8.0.4", "8.1.0", false, "8.1.0")]
    [TestCase(PackagePolicy.Minor, "8.0.4", "9.0.0", false, "8.0.4")]
    [TestCase(PackagePolicy.Minor, "8.0.4", "9.0.0", true, "9.0.0")]
    [TestCase(PackagePolicy.Patch, "8.0.4", "8.0.11", false, "8.0.11")]
    [TestCase(PackagePolicy.Patch, "8.0.4", "8.1.0", false, "8.0.4")]
    [TestCase(PackagePolicy.None, "8.0.4", "8.0.11", false, "8.0.4")]
    public void APackage_TakesWhatThePolicyLets(string updates, string had, string brought, bool majorUpgrade, string takes)
    {
        new PackagePolicy { Updates = updates }.Decide("Npgsql", had, brought, majorUpgrade).Version.ShouldBe(takes);
    }

    [Test]
    public void APinnedPackage_KeepsItsVersion_AndTheReportSaysWhatTheTemplateBrings()
    {
        var decision = new PackagePolicy { Pinned = new() { ["Npgsql"] = "8.0.4" } }.Decide("Npgsql", "8.0.4", "8.0.11", false);

        decision.Version.ShouldBe("8.0.4");
        decision.Line.ShouldBe("pinned on 8.0.4, the template brings 8.0.11");
    }

    [Test]
    public void APin_MatchesThePackageInAnyCase_AndAPinOfNoPackageOfTheTemplateIsReported()
    {
        var before = Folders.NewTemp("p-base");
        var after = Folders.NewTemp("p-next");
        Put(before, Central, Props(("Npgsql", "8.0.4")));
        Put(after, Central, Props(("Npgsql", "8.0.11")));
        var policy = new PackagePolicy { Pinned = new() { ["npgsql"] = "8.0.4", ["Npgslq"] = "1.0.0" } };

        var lines = PackageVersions.Hold(before, after, policy, majorUpgrade: false);

        PackageVersions.Read(File.ReadAllText(Path.Combine(after, Central)))["Npgsql"].ShouldBe("8.0.4");
        lines.ShouldContain("Npgslq: pinned on 1.0.0, not among the template's packages");
    }

    [Test]
    public void AVersion_IsGivenOnlyTheParametersItHas()
    {
        var args = TemplateArgs.Parse(["-N", "Acme", "--Agent", "none", "--Storage", "true", "--MongoDB", "true"]);

        args.ForVersion("2.0.0").ToArgs().ShouldBe(["--NamespaceRoot", "Acme"]);
        args.ForVersion("2.7.0").ToArgs().ShouldBe(["--NamespaceRoot", "Acme", "--Agent", "none", "--Storage", "true"]);
        args.ForVersion(TemplateSource.SourcesVersion).ToArgs().Count.ShouldBe(8);
    }

    [TestCase("1.0.0")]
    [TestCase("2.0.0")]
    [TestCase("2.6.0")]
    public void AReleaseBeforeNuGet_IsDescribedAndUpgraded_FromItsTag(string version)
    {
        TemplateVersions.WhyNotDescribed(version, "2.9.0", onSources: false).ShouldBeNull();
        TemplateVersions.WhyNotUpgraded(version, "2.9.0", onSources: false).ShouldBeNull();
        TemplateVersions.IsPublished(version).ShouldBeFalse();
        TemplateVersions.IsPublished("2.6.1").ShouldBeTrue();
        TemplateVersions.IsPublished(TemplateSource.SourcesVersion).ShouldBeFalse();
    }

    [Test]
    public void TheReleaseToGoTo_IsNewerThanTheSolution_AndNotNewerThanTheTool()
    {
        TemplateVersions.WhyNotTarget("1.0.0", "2.0.0", "2.9.0", onSources: false).ShouldBeNull();
        TemplateVersions.WhyNotTarget("2.3.0", "2.1.0", "2.9.0", onSources: false)!.ShouldContain("not older than 2.1.0");
        TemplateVersions.WhyNotTarget("2.3.0", "3.0.0", "2.9.0", onSources: false)!.ShouldContain("newer than dotskit 2.9.0");
        TemplateVersions.WhyNotTarget("2.3.0", "3.0.0", "2.9.0", onSources: true).ShouldBeNull();
        TemplateVersions.WhyNotTarget("2.3.0", TemplateSource.SourcesVersion, "2.9.0", onSources: true)!.ShouldContain("not a release");
        TemplateVersions.WhyNotTarget("2.1.0", "2.3", "2.9.0", onSources: false)!.ShouldContain("not a release");
    }

    [Test]
    public void TheReleaseToGoTo_IsAnOptionOfUpgradeOnly()
    {
        Options.Parse("upgrade", ["--to", "2.3.0", "--yes"]).To.ShouldBe("2.3.0");
        Should.Throw<ArgumentException>(() => Options.Parse("init", ["--to", "2.3.0"])).Message.ShouldContain("option of dotskit upgrade");
        Should.Throw<ArgumentException>(() => Options.Parse("upgrade", ["--to"])).Message.ShouldContain("needs a value");
    }

    [Test]
    public void ASolutionMajorsBehind_GetsEachMajorsStep_FromItsOwn()
    {
        var steps = TemplateVersions.WhyNotUpgraded("2.6.1", "4.0.0", onSources: false)!;

        steps.ShouldContain("--version 2.*, dotskit upgrade; then dotnet tool update -g SawKing.DotsKit.Tool --version 3.*, dotskit upgrade; then dotnet tool update -g SawKing.DotsKit.Tool, dotskit upgrade.");
    }

    [Test]
    public void APackageNewToTheSolution_ComesAsTheTemplateHasIt()
    {
        new PackagePolicy { Updates = PackagePolicy.None }.Decide("MongoDB.Driver", null, "3.12.0", false).Version.ShouldBe("3.12.0");
    }

    [Test]
    public void TheVersionsHeldBack_AreWrittenIntoTheTemplatesSide_SoTheirLinesDoNotConflict()
    {
        var before = Folders.NewTemp("p-base");
        var after = Folders.NewTemp("p-next");
        Put(before, Central, Props(("Npgsql", "8.0.4"), ("Serilog", "4.0.0")));
        Put(after, Central, Props(("Npgsql", "8.0.11"), ("Serilog", "5.0.0")));

        var lines = PackageVersions.Hold(before, after, new PackagePolicy { Pinned = new() { ["Npgsql"] = "8.0.4" } }, majorUpgrade: false);

        PackageVersions.Read(File.ReadAllText(Path.Combine(after, Central))).ShouldBe(new Dictionary<string, string> { ["Npgsql"] = "8.0.4", ["Serilog"] = "4.0.0" });
        lines.ShouldBe([
            "Npgsql: pinned on 8.0.4, the template brings 8.0.11",
            "Serilog: held on 4.0.0 by packages.updates \"minor\", the template brings 5.0.0",
        ]);
    }

    [Test]
    public void AnUnknownPolicy_IsRefusedWhenTheManifestIsRead()
    {
        const string manifest = """{ "template": "2.8.0", "solution": [], "services": [["--ServiceNameOrCustom", "Orders"]], "packages": { "updates": "major" } }""";

        Should.Throw<InvalidOperationException>(() => Manifest.Parse(manifest)).Message.ShouldContain("packages.updates");
    }

    [Test]
    public void TheManifest_ExplainsItsFieldsInPlace_AndReadsBackTheSame()
    {
        var manifest = new Manifest { Template = "2.8.0", Solution = [], Services = [["--ServiceNameOrCustom", "Orders"]] };

        var text = manifest.Serialize();

        text.ShouldContain("\"_comment_template\"");
        text.ShouldContain("\"_comment_pinned\"");
        Manifest.Parse(text).Serialize().ShouldBe(text);
    }

    [Test]
    public void GitsRenames_AreReadFromItsNameStatusOutput()
    {
        // As git writes it: a removal and a change name one path, a rename two.
        var output = "D\0C:/t/base/b.cs\0M\0C:/t/base/a.cs\0R087\0C:/t/base/src/common/X.cs\0C:/t/next/src/capabilities/X.cs\0";

        GitRenames.Parse(output, "C:/t/base", "C:/t/next").ShouldBe(new Dictionary<string, string> { ["src/capabilities/X.cs"] = "src/common/X.cs" });
    }

    [Test]
    public void AMoveGitPairsAcrossTwoServices_IsTakenToTheSameMoveInsideTheOldPathsService()
    {
        // Both services moved Stabs/ to Stubs/; git paired Orders' old file with Billing's new one.
        const string orders = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Tests/Misc/";
        const string billing = "src/services/Acme.Shop.Billing/Acme.Shop.Billing.Tests/Misc/";
        var renames = new Dictionary<string, string>
        {
            [billing + "Stubs/UserContextMock.cs"] = orders + "Stabs/UserContextMock.cs",
            ["src/common/Web/Swagger.cs"] = orders + "Setup/Swagger.cs",
            [billing + "Stubs/Lost.cs"] = orders + "Stabs/Lost.cs",
        };
        string[] files = [orders + "Stubs/UserContextMock.cs", billing + "Stubs/UserContextMock.cs", "src/common/Web/Swagger.cs"];

        GitRenames.WithinServices(renames, files.Contains).ShouldBe(new Dictionary<string, string>
        {
            [orders + "Stubs/UserContextMock.cs"] = orders + "Stabs/UserContextMock.cs",
            ["src/common/Web/Swagger.cs"] = orders + "Setup/Swagger.cs",
        }, ignoreOrder: true);
    }

    [Test]
    public async Task AFileTheTemplateMoved_TakesTheTeamsChangeToItsNewPath()
    {
        var solution = Folders.NewTemp("m-solution");
        var before = Folders.NewTemp("m-base");
        var after = Folders.NewTemp("m-next");
        Put(before, "src/common/X.cs", "a\nb\nc\nd\ne\n");
        Put(solution, "src/common/X.cs", "a\nb\nc\nd\ne\nteam\n");
        Put(after, "src/capabilities/X.cs", "template\na\nb\nc\nd\ne\n");
        var renames = new Dictionary<string, string> { ["src/capabilities/X.cs"] = "src/common/X.cs" };

        var changes = await new ThreeWayMerge(new GitMergeFile(new ProcessRunner())).ComputeAsync(solution, before, after, false, renames, CancellationToken.None);

        var moved = changes.Single(c => c.Path == "src/capabilities/X.cs");
        moved.Kind.ShouldBe(ChangeKind.Merge);
        moved.Content!.Text.ShouldBe("template\na\nb\nc\nd\ne\nteam\n");
        moved.Reason.ShouldStartWith("moved from src/common/X.cs");
        changes.Single(c => c.Path == "src/common/X.cs").Kind.ShouldBe(ChangeKind.Delete);
    }

    [Test]
    public void TheBuildsErrors_AreListedOncePerProject()
    {
        const string output = """
            C:\s\src\Orders.API\Program.cs(3,1): error CS0246: The type 'Foo' could not be found [C:\s\src\Orders.API\Orders.API.csproj]
            C:\s\src\Orders.API\Program.cs(3,1): error CS0246: The type 'Foo' could not be found [C:\s\src\Orders.API\Orders.API.csproj]
            C:\s\src\Common\Common.csproj : error NU1015: The following PackageReference item(s) do not have a version specified: Npgsql [C:\s\src\All.sln]
            """;

        BuildErrors.ByProject(output).ShouldBe([
            "  Common",
            "    Common.csproj: NU1015 The following PackageReference item(s) do not have a version specified: Npgsql",
            "  Orders.API",
            "    Program.cs: CS0246 The type 'Foo' could not be found",
        ]);
    }

    [Test]
    public void TheGuide_NamesTheMinorVersionsCrossed_AndTheUpgradingPageOfTheBranchTheTargetIsOn()
    {
        UpgradeGuide.Of("1.0.0", "2.8.0").ShouldBe(
            "What a solution may have to change by hand, from v1.0 to v2.8: the sections for these versions of "
            + "https://github.com/sawking-tech/DotNetSolutionKit/blob/master/docs/getting-started/upgrading.md");
        UpgradeGuide.Of("2.8.0", "2.9.0-rc").ShouldEndWith("/blob/dev/docs/getting-started/upgrading.md");
        UpgradeGuide.Of("2.8.0", TemplateSource.SourcesVersion).ShouldContain($"to {TemplateSource.SourcesVersion}: ");
    }
}
