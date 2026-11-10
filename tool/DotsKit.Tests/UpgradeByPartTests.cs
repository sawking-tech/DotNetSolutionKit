using DotsKit.Io;
using DotsKit.Planning;

namespace DotsKit.Tests;

/// <summary>An upgrade a part at a time: which part a path is in, which part a run takes, the trees of a step, the manifest between parts.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class UpgradeByPartTests
{
    private const string Orders = "ST.Demo.Orders";
    private const string Billing = "ST.Demo.Billing";

    [Test]
    public void APath_IsInItsServiceFolder_OrInTheSharedPart()
    {
        UpgradeParts.Of($"src/services/{Orders}/{Orders}.API/Setup/ApplicationSetup.cs").ShouldBe(Orders);
        UpgradeParts.Of("src/common/ST.Demo.Common/Domain/Entity.cs").ShouldBe("common/ST.Demo.Common");
        UpgradeParts.Of("src/common/Directory.Build.props").ShouldBe(UpgradeParts.Common);
        UpgradeParts.Of("src/capabilities/ST.Demo.Capabilities.Persistence.Mongo/MongoSetup.cs").ShouldBe(UpgradeParts.Capabilities);
        UpgradeParts.Of("src/Directory.Packages.props").ShouldBe(UpgradeParts.Shared);
        UpgradeParts.Of("src/services/ST.Demo.All.sln").ShouldBe(UpgradeParts.Shared);
        UpgradeParts.Of("deploy/compose/compose.yml").ShouldBe(UpgradeParts.Shared);
        UpgradeParts.OrderablePartOf("src/common/ST.Demo.Common.Infrastructure/ST.Demo.Common.Infrastructure.csproj").ShouldBe("common/ST.Demo.Common.Infrastructure");
        UpgradeParts.OrderablePartOf("src/capabilities/ST.Demo.Capabilities.Analytics.ClickHouse/ST.Demo.Capabilities.Analytics.ClickHouse.csproj").ShouldBe(UpgradeParts.Capabilities);
        UpgradeParts.OrderablePartOf($"src/services/{Orders}/{Orders}.API/{Orders}.API.csproj").ShouldBeNull();
        UpgradeParts.OrderablePartOf("Directory.Build.props").ShouldBeNull();
    }

    [Test]
    public void TheParts_AreTheSharedOneFirst_ThenTheServiceFoldersInOrder()
    {
        UpgradeParts.In([$"src/services/{Orders}/a.cs", "Directory.Build.props", $"src/services/{Billing}/b.cs", "src/common/ST.Demo.Common.Web/w.cs",
                "src/common/ST.Demo.Common/c.cs", "src/capabilities/ST.Demo.Capabilities.Persistence.Mongo/m.cs"])
            .ShouldBe([UpgradeParts.Shared, "common/ST.Demo.Common", "common/ST.Demo.Common.Web", UpgradeParts.Capabilities, Billing, Orders]);
    }

    [Test]
    public void TheProjectsOfCommon_ThatReferenceNoOther_GoByLayer_FromTheDomainToTheTests()
    {
        string[] paths = ["src/common/ST.Demo.Common.Tests/t.cs", "src/common/ST.Demo.Common.Web/w.cs", "src/common/ST.Demo.Common.Infrastructure/i.cs",
            "src/common/ST.Demo.Common.Application/a.cs", "src/common/ST.Demo.Common.Contracts/c.cs", "src/common/ST.Demo.Common/d.cs"];

        UpgradeParts.In(paths).ShouldBe([UpgradeParts.Shared, "common/ST.Demo.Common", "common/ST.Demo.Common.Contracts", "common/ST.Demo.Common.Application",
            "common/ST.Demo.Common.Infrastructure", "common/ST.Demo.Common.Web", "common/ST.Demo.Common.Tests"]);
    }

    [Test]
    public void TheProjectsOfCommon_GoBeforeThoseThatReferenceThem_ThenByLayer()
    {
        string[] paths = ["src/common/Directory.Build.props", "src/common/ST.Demo.Common.Application/a.cs", "src/common/ST.Demo.Common.Web/w.cs",
            "src/common/ST.Demo.Common.Domain/d.cs"];
        var references = new Dictionary<string, IReadOnlySet<string>>
        {
            ["common/ST.Demo.Common.Application"] = new HashSet<string> { "common/ST.Demo.Common.Web" },
            ["common/ST.Demo.Common.Web"] = new HashSet<string> { "common/ST.Demo.Common.Domain" },
        };

        UpgradeParts.In(paths, references).ShouldBe([UpgradeParts.Shared, UpgradeParts.Common, "common/ST.Demo.Common.Domain", "common/ST.Demo.Common.Web",
            "common/ST.Demo.Common.Application"]);
    }

    [Test]
    public void TheCapabilities_ByDefaultGoAfterTheProjectsOfCommon()
    {
        string[] paths = ["src/common/ST.Demo.Common.Infrastructure/i.cs", "src/capabilities/ST.Demo.Capabilities.Analytics.ClickHouse/c.cs",
            "src/common/ST.Demo.Common.Web/w.cs"];

        UpgradeParts.In(paths).ShouldBe([UpgradeParts.Shared, "common/ST.Demo.Common.Infrastructure", "common/ST.Demo.Common.Web", UpgradeParts.Capabilities]);
    }

    [Test]
    public void TheCapabilities_GoBeforeTheProjectOfCommonATypeLeavesToThem()
    {
        string[] paths = ["src/common/ST.Demo.Common.Infrastructure/i.cs", "src/capabilities/ST.Demo.Capabilities.Analytics.ClickHouse/c.cs",
            "src/common/ST.Demo.Common.Web/w.cs"];
        var references = new Dictionary<string, IReadOnlySet<string>>
        {
            ["common/ST.Demo.Common.Infrastructure"] = new HashSet<string> { UpgradeParts.Capabilities },
        };

        UpgradeParts.In(paths, references).ShouldBe([UpgradeParts.Shared, "common/ST.Demo.Common.Web", UpgradeParts.Capabilities,
            "common/ST.Demo.Common.Infrastructure"]);
    }

    [Test]
    public void AProjectOfCommon_GoesBeforeTheCapabilitiesATypeLeavesToIt()
    {
        string[] paths = ["src/common/ST.Demo.Common.Domain/d.cs", "src/capabilities/ST.Demo.Capabilities.Analytics.ClickHouse/c.cs"];
        var references = new Dictionary<string, IReadOnlySet<string>>
        {
            [UpgradeParts.Capabilities] = new HashSet<string> { "common/ST.Demo.Common.Domain" },
        };

        UpgradeParts.In(paths, references).ShouldBe([UpgradeParts.Shared, "common/ST.Demo.Common.Domain", UpgradeParts.Capabilities]);
    }

    [Test]
    public void APathOfNoPartOfTheUpgrade_GoesWithTheSharedPart_AndAPartOfTheUpgradeWaitsForItsTurn()
    {
        var step = new PartStep(new HashSet<string>(), UpgradeParts.Shared, [UpgradeParts.Shared, "common/ST.Demo.Common", "common/ST.Demo.Common.Domain", Orders]);

        step.InDoneOrThis("src/common/ST.Demo.Common.Domain/d.cs").ShouldBeFalse();
        step.InDoneOrThis("src/common/ST.Demo.Common.Web/w.cs").ShouldBeTrue();
        step.InDoneOrThis("src/common/ST.Demo.Common/c.cs").ShouldBeFalse();
    }

    [Test]
    public void ARun_TakesTheFirstPartLeft_OrTheOneNamed_AndTheSharedOneGoesFirst()
    {
        IReadOnlyList<string> parts = [UpgradeParts.Shared, UpgradeParts.Capabilities, "common/ST.Demo.Common", Billing, Orders];
        string[] shared = [UpgradeParts.Shared, UpgradeParts.Capabilities, "common/ST.Demo.Common"];

        UpgradeParts.Next(parts, [], null).ShouldBe(UpgradeParts.Shared);
        UpgradeParts.Next(parts, [UpgradeParts.Shared], null).ShouldBe(UpgradeParts.Capabilities);
        UpgradeParts.Next(parts, shared, null).ShouldBe(Billing);
        UpgradeParts.Next(parts, shared, "Orders").ShouldBe(Orders);
        Should.Throw<InvalidOperationException>(() => UpgradeParts.Next(parts, [UpgradeParts.Shared], "Orders")).Message.ShouldContain("shared parts go first");
        Should.Throw<InvalidOperationException>(() => UpgradeParts.Next(parts, [UpgradeParts.Shared], "shared")).Message.ShouldContain("merged already");
        Should.Throw<ArgumentException>(() => UpgradeParts.Next(parts, [], "Stock")).Message.ShouldContain("no part of this upgrade");
        UpgradeParts.Next(parts, [], "Shared").ShouldBe(UpgradeParts.Shared);
        Should.Throw<InvalidOperationException>(() => UpgradeParts.Next(parts, [.. parts], "Orders")).Message.ShouldContain("without --part Orders, finishes it");
    }

    [Test]
    public void APartNamedByTheEndOfItsName_IsTakenOnlyWhenNoOtherPartEndsSo()
    {
        IReadOnlyList<string> parts = [UpgradeParts.Shared, UpgradeParts.Capabilities, "ST.Billing.Orders", Orders];
        string[] shared = [UpgradeParts.Shared, UpgradeParts.Capabilities];

        Should.Throw<ArgumentException>(() => UpgradeParts.Next(parts, shared, "Orders")).Message
            .ShouldBe($"--part Orders names ST.Billing.Orders, {Orders}: name one of them in full.");
        UpgradeParts.Next(parts, shared, Orders).ShouldBe(Orders);
        UpgradeParts.Next(parts, shared, "Billing.Orders").ShouldBe("ST.Billing.Orders");
    }

    [Test]
    public void TheFoldersOfAnIde_BesideAllSln_AreNoParts_AndASolutionWithoutCapabilitiesHasNoSuchPart()
    {
        var root = Tree(($"src/services/{Orders}/a.cs", "x"), ("src/services/.vs/ST/v17/.suo", "x"), ("src/services/.idea/workspace.xml", "x"));

        UpgradeParts.In(Folders.RelativeFiles(root)).ShouldBe([UpgradeParts.Shared, Orders]);
    }

    [Test]
    public void AnUpgrade_GoesAPartAtATime_UnlessAllAtOnce_AnOptionOfUpgradeOnly()
    {
        Options.Parse("upgrade", ["--yes"]).AllAtOnce.ShouldBeFalse();
        Options.Parse("upgrade", ["--all-at-once"]).AllAtOnce.ShouldBeTrue();
        Options.Parse("upgrade", ["--all-at-once"]).TemplateArgs.ShouldBeEmpty();
        Should.Throw<ArgumentException>(() => Options.Parse("new", ["--all-at-once"])).Message.ShouldContain("options of dotskit upgrade");
        Should.Throw<ArgumentException>(() => Options.Parse("new", ["--part", "Orders"])).Message.ShouldContain("options of dotskit upgrade");
        Should.Throw<ArgumentException>(() => Options.Parse("upgrade", ["--all-at-once", "--part", "Orders"])).Message.ShouldContain("leave out one of them");
    }

    [Test]
    public void TheTreeOfAStep_TakesTheNextFilesOfItsParts_AndTheBaseFilesOfTheRest()
    {
        var baseDir = Tree(("Directory.Build.props", "base"), ($"src/services/{Orders}/a.cs", "base"), ($"src/services/{Orders}/gone.cs", "base"),
            ($"src/services/{Billing}/b.cs", "base"));
        var nextDir = Tree(("Directory.Build.props", "next"), ($"src/services/{Orders}/a.cs", "next"), ($"src/services/{Orders}/new.cs", "next"),
            ($"src/services/{Billing}/b.cs", "next"));
        var step = new PartStep(new HashSet<string> { UpgradeParts.Shared }, Orders);

        var tree = Folders.Overlay(baseDir, nextDir, step.InDoneOrThis);

        Read(tree, "Directory.Build.props").ShouldBe("next");
        Read(tree, $"src/services/{Orders}/a.cs").ShouldBe("next");
        Read(tree, $"src/services/{Orders}/new.cs").ShouldBe("next");
        File.Exists(Path.Combine(tree, $"src/services/{Orders}/gone.cs")).ShouldBeFalse();
        Read(tree, $"src/services/{Billing}/b.cs").ShouldBe("base");
    }

    [Test]
    public void TheManifest_KeepsThePartsMerged_AndTheLastMovesTheVersion()
    {
        var manifest = new Manifest { Template = "2.0.0", Solution = ["--NamespaceRoot", "ST"], Services = [["--ServiceNameOrCustom", "Orders"]] };

        string[] parts = [UpgradeParts.Shared, "common/ST.Demo.Common.Domain", Orders];
        var between = Manifest.Parse(manifest.WithPartMerged("2.7.0", UpgradeParts.Shared, last: false, parts).Serialize());
        var done = between.WithPartMerged("2.7.0", Orders, last: true, parts);
        var again = between.WithPartMerged("2.7.0", UpgradeParts.Shared, last: false, parts);

        again.Upgrade!.Done.ShouldBe([UpgradeParts.Shared]);
        between.Upgrade!.Parts.ShouldBe(parts);
        between.Serialize().ShouldContain("\"_comment_parts\"");

        between.Template.ShouldBe("2.0.0");
        between.Upgrade!.To.ShouldBe("2.7.0");
        between.Upgrade.Done.ShouldBe([UpgradeParts.Shared]);
        between.Serialize().ShouldContain("\"_comment_upgrade\"");
        done.Template.ShouldBe("2.7.0");
        done.Upgrade.ShouldBeNull();
        done.Serialize().ShouldNotContain("upgrade\"");
    }

    [Test]
    public void TheManifestOfAStoppedPart_KeepsItOutOfDone_UntilItsMergeIsCommitted()
    {
        var manifest = new Manifest { Template = "2.0.0", Solution = ["--NamespaceRoot", "ST"], Services = [["--ServiceNameOrCustom", "Orders"]] };

        string[] parts = [UpgradeParts.Shared, "common/ST.Demo.Common.Domain", Orders];
        var between = Manifest.Parse(manifest.WithPartMerged("2.7.0", UpgradeParts.Shared, last: false, parts).Serialize());
        var stopped = Manifest.Parse(between.WithPartInConflict("2.7.0", parts).Serialize());

        stopped.Template.ShouldBe("2.0.0");
        stopped.Upgrade!.To.ShouldBe("2.7.0");
        stopped.Upgrade.Parts.ShouldBe(parts);
        stopped.Upgrade.Done.ShouldBe([UpgradeParts.Shared], "the part in conflict is no part of Done until its merge is committed");
        stopped.Serialize().ShouldContain("\"_comment_upgrade\"");
    }

    private static string Tree(params (string Path, string Text)[] files)
    {
        var root = Folders.NewTemp("parts");
        foreach (var (path, text) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
            File.WriteAllText(Path.Combine(root, path), text);
        }

        return root;
    }

    private static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path));
}

