using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>The moves a part of an upgrade follows, and the report of what the repair changed, by project.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class RepairReportTests
{
    private const string Domain = "src/common/ST.Demo.Common.Domain/ST.Demo.Common.Domain.csproj";
    private const string Application = "src/common/ST.Demo.Common.Application/ST.Demo.Common.Application.csproj";

    /// <summary>The event moves from Common.Application to Common.Domain: two parts of the upgrade.</summary>
    private static readonly TypeMap Moved = TypeMap.Between(
        TypeIndex.Of([(Application, "namespace ST.Demo.Common.Application.Events;\npublic sealed class DomainEvent;\n")]),
        TypeIndex.Of([(Domain, "namespace ST.Demo.Common.Domain.Events;\npublic sealed class DomainEvent;\n")]));

    [Test]
    public void AMoveBetweenTwoParts_IsFollowedOnceItsTargetIsMerged_AndItsNamespaceIsKeptTill()
    {
        var applicationOnly = Moved.Within(project => UpgradeParts.Of(project) == "common/ST.Demo.Common.Application");
        var domainOnly = Moved.Within(project => UpgradeParts.Of(project) == "common/ST.Demo.Common.Domain");
        var both = Moved.Within(project => UpgradeParts.Of(project).StartsWith("common/", StringComparison.Ordinal));

        Moved.OutOf("ST.Demo.Common.Application.Events").ShouldHaveSingleItem();
        applicationOnly.Moves.ShouldBeEmpty();
        applicationOnly.OutOf("ST.Demo.Common.Application.Events").ShouldBeEmpty();
        domainOnly.ShouldBeSameAs(Moved);
        both.ShouldBeSameAs(Moved);
    }

    [Test]
    public void TheReport_IsALinePerProject_WithEachChangeOnceAndTheFilesItTook()
    {
        var root = Io.Folders.NewTemp("repair-report");
        var project = Path.Combine(root, "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/ST.Demo.Orders.Domain.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "<Project />");
        var order = Path.Combine(Path.GetDirectoryName(project)!, "Order.cs");
        var refund = Path.Combine(Path.GetDirectoryName(project)!, "Refunds", "Refund.cs");

        var lines = RepairReport.ByProject([
            new ReplaceUsing(order, "ST.Demo.Common.Events", "ST.Demo.Common.Domain.Events"),
            new ReplaceUsing(refund, "ST.Demo.Common.Events", "ST.Demo.Common.Domain.Events"),
            new AddUsing(order, "ST.Demo.Common.Domain"),
            new AddReference(project, Path.Combine(root, Domain)),
        ]);

        lines.ShouldBe(["  ST.Demo.Orders.Domain: using ST.Demo.Common.Events -> ST.Demo.Common.Domain.Events (2 files); using ST.Demo.Common.Domain added; reference to ST.Demo.Common.Domain"]);
    }
}

