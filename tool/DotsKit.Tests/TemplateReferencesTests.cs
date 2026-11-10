using DotsKit.Io;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>The references between the template's projects change as the template changes them; the team's stay.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class TemplateReferencesTests
{
    private const string Common = "src/common/ST.Demo.Common/ST.Demo.Common.csproj";
    private const string Contracts = "src/common/ST.Demo.Common.Contracts/ST.Demo.Common.Contracts.csproj";
    private const string Orders = "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/ST.Demo.Orders.Domain.csproj";
    private const string Billing = "src/services/ST.Demo.Billing/ST.Demo.Billing.Contracts/ST.Demo.Billing.Contracts.csproj";

    private const string ToCommon = @"..\ST.Demo.Common\ST.Demo.Common.csproj";
    private const string ToContracts = @"..\ST.Demo.Common.Contracts\ST.Demo.Common.Contracts.csproj";
    private const string OrdersToCommon = @"..\..\..\common\ST.Demo.Common\ST.Demo.Common.csproj";
    private const string OrdersToContracts = @"..\..\..\common\ST.Demo.Common.Contracts\ST.Demo.Common.Contracts.csproj";
    private const string OrdersToBilling = @"..\..\ST.Demo.Billing\ST.Demo.Billing.Contracts\ST.Demo.Billing.Contracts.csproj";

    private static string Project(params string[] references) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n"
        + string.Concat(references.Select(r => $"    <ProjectReference Include=\"{r}\" />\n"))
        + "  </ItemGroup>\n</Project>\n";

    private static string Tree(params (string Path, string Text)[] files)
    {
        var root = Folders.NewTemp("refs");
        foreach (var (path, text) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
            File.WriteAllText(Path.Combine(root, path), text);
        }

        return root;
    }

    [Test]
    public void TheTemplatesOwnChange_IsCarried_AndTheTeamsReferenceBetweenProjectsOfTheTemplateStays()
    {
        // The template 1.0: Common referenced Contracts; 2.0 turned it round and Orders gained Contracts.
        var before = Tree((Common, Project(ToContracts)), (Contracts, Project()), (Orders, Project(OrdersToCommon)), (Billing, Project()));
        var next = Tree((Common, Project()), (Contracts, Project(ToCommon)), (Orders, Project(OrdersToCommon, OrdersToContracts)), (Billing, Project()));
        // The team: Orders also uses the contracts of Billing, a project of the template too.
        var root = Tree((Common, Project(ToContracts)), (Contracts, Project()), (Orders, Project(OrdersToCommon, OrdersToBilling)), (Billing, Project()));
        string Full(string path) => Path.GetFullPath(Path.Combine(root, path));

        var repairs = TemplateReferences.Decide(root, before, next, new HashSet<string>());

        repairs.ShouldBe([
            new RemoveReference(Full(Common), Full(Contracts)),
            new AddReference(Full(Contracts), Full(Common)),
            new AddReference(Full(Orders), Full(Contracts)),
        ], ignoreOrder: true);
        TemplateReferences.Decide(root, before, next, new HashSet<string> { Common, Contracts, Orders }).ShouldBeEmpty();
    }

    [Test]
    public void AReferenceIsAddedAndRemovedInTheProjectsText_WithNoMSBuild_AndItsLineEndsKept()
    {
        var root = Folders.NewTemp("refs-text");
        var orders = Path.Combine(root, Orders);
        var text = Project(OrdersToCommon).Replace("\n", "\r\n", StringComparison.Ordinal);

        var added = ProjectReferences.Add(text, orders, Path.Combine(root, Contracts));
        var removed = ProjectReferences.Remove(added, orders, Path.Combine(root, Common));

        added.ShouldContain($"    <ProjectReference Include=\"{OrdersToCommon}\" />\r\n    <ProjectReference Include=\"{OrdersToContracts}\" />\r\n");
        ProjectReferences.Add(added, orders, Path.Combine(root, Contracts)).ShouldBe(added);
        removed.ShouldNotContain("ST.Demo.Common\\ST.Demo.Common.csproj");
        removed.ShouldContain(OrdersToContracts);
    }
}

