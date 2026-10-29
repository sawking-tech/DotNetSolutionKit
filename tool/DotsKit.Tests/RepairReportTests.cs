using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>The moves a part of an upgrade follows, and the report of what the repair changed, by project.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class RepairReportTests
{
    private const string Domain = "src/common/Acme.Shop.Common.Domain/Acme.Shop.Common.Domain.csproj";
    private const string Application = "src/common/Acme.Shop.Common.Application/Acme.Shop.Common.Application.csproj";

    /// <summary>The event moves from Common.Application to Common.Domain: two parts of the upgrade.</summary>
    private static readonly TypeMap Moved = TypeMap.Between(
        TypeIndex.Of([(Application, "namespace Acme.Shop.Common.Application.Events;\npublic sealed class DomainEvent;\n")]),
        TypeIndex.Of([(Domain, "namespace Acme.Shop.Common.Domain.Events;\npublic sealed class DomainEvent;\n")]));

    [Test]
    public void AMoveBetweenTwoParts_IsFollowedOnceBothAreMerged_AndItsNamespaceIsKeptTill()
    {
        var applicationOnly = Moved.Within(project => UpgradeParts.Of(project) == "common/Acme.Shop.Common.Application");
        var both = Moved.Within(project => UpgradeParts.Of(project).StartsWith("common/", StringComparison.Ordinal));

        Moved.OutOf("Acme.Shop.Common.Application.Events").ShouldHaveSingleItem();
        applicationOnly.Moves.ShouldBeEmpty();
        applicationOnly.OutOf("Acme.Shop.Common.Application.Events").ShouldBeEmpty();
        both.ShouldBeSameAs(Moved);
    }

    [Test]
    public void TheReport_IsALinePerProject_WithEachChangeOnceAndTheFilesItTook()
    {
        var root = Io.Folders.NewTemp("repair-report");
        var project = Path.Combine(root, "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Acme.Shop.Orders.Domain.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, "<Project />");
        var order = Path.Combine(Path.GetDirectoryName(project)!, "Order.cs");
        var refund = Path.Combine(Path.GetDirectoryName(project)!, "Refunds", "Refund.cs");

        var lines = RepairReport.ByProject([
            new ReplaceUsing(order, "Acme.Shop.Common.Events", "Acme.Shop.Common.Domain.Events"),
            new ReplaceUsing(refund, "Acme.Shop.Common.Events", "Acme.Shop.Common.Domain.Events"),
            new AddUsing(order, "Acme.Shop.Common.Domain"),
            new AddReference(project, Path.Combine(root, Domain)),
        ]);

        lines.ShouldBe(["  Acme.Shop.Orders.Domain: using Acme.Shop.Common.Events -> Acme.Shop.Common.Domain.Events (2 files); using Acme.Shop.Common.Domain added; reference to Acme.Shop.Common.Domain"]);
    }
}
