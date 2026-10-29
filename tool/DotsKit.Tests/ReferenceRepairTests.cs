using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>
/// The repair of an upgrade for the types the template moved (#117): the index of where a type lives, the namespaces
/// the template gave up, the repairs a build's errors ask for, and the using lines they write.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class ReferenceRepairTests
{
    private const string Root = "/s";
    private const string Common = "src/common/Acme.Shop.Common/Acme.Shop.Common.csproj";
    private const string Framework = "src/framework/Acme.Shop.Framework.Events/Acme.Shop.Framework.Events.csproj";
    private const string Orders = "/s/src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Acme.Shop.Orders.Domain.csproj";
    private const string OrderFile = "/s/src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Order.cs";

    private static readonly TypeIndex Before = TypeIndex.Of([
        (Common, "namespace Acme.Shop.Common.Events;\n\npublic sealed class DomainEvent;\n"),
        (Common, "namespace Acme.Shop.Common\n{\n    public static class Marker { }\n}\n"),
    ]);

    private static readonly TypeIndex After = TypeIndex.Of([
        (Framework, "namespace Acme.Shop.Framework.Events;\n\npublic sealed class DomainEvent;\n"),
        (Common, "namespace Acme.Shop.Common\n{\n    public static class Marker { }\n}\n"),
    ]);

    private static BuildError Error(string code, string message) => new(OrderFile, code, message, Orders);

    /// <summary>Order.cs as the team wrote it, with the using of the namespace the template gave up.</summary>
    private static IReadOnlyList<string> Usings(string file) => file == OrderFile ? ["System", "Acme.Shop.Common.Events"] : [];

    [Test]
    public void TheIndex_HoldsThePublicTypes_WithTheirProjectAndNamespace_AndLeavesOutTheInternalOnes()
    {
        var index = TypeIndex.Of([(Common, "namespace Acme.Shop.Common;\n\npublic sealed record Money(decimal Amount);\npublic interface IClock { }\ninternal sealed class Hidden { }\n")]);

        index.Find("Money").ShouldBe([new TypeHome("Money", "Acme.Shop.Common", Common)]);
        index.Find("IClock").ShouldHaveSingleItem();
        index.Find("Hidden").ShouldBeEmpty();
    }

    [Test]
    public void AMovedType_IsFollowedToItsNewHome_AndAKeptOneIsNoMove()
    {
        var map = TypeMap.Between(Before, After);

        map.Moves.ShouldBe([new TypeMove(
            new TypeHome("DomainEvent", "Acme.Shop.Common.Events", Common),
            new TypeHome("DomainEvent", "Acme.Shop.Framework.Events", Framework))]);
        map.OutOf("Acme.Shop.Common.Events").ShouldHaveSingleItem();
        map.OutOf("Acme.Shop.Common").ShouldBeEmpty();
    }

    [Test]
    public void AServicesCopyOfAMovedType_LeadsToItsOwnServiceOnly()
    {
        const string ordersApp = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Application/Acme.Shop.Orders.Application.csproj";
        const string ordersDomain = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Acme.Shop.Orders.Domain.csproj";
        const string billingApp = "src/services/Acme.Shop.Billing/Acme.Shop.Billing.Application/Acme.Shop.Billing.Application.csproj";
        const string billingDomain = "src/services/Acme.Shop.Billing/Acme.Shop.Billing.Domain/Acme.Shop.Billing.Domain.csproj";
        const string ordersFile = "/s/src/services/Acme.Shop.Orders/Acme.Shop.Orders.API/Endpoint.cs";
        const string ordersApi = "/s/src/services/Acme.Shop.Orders/Acme.Shop.Orders.API/Acme.Shop.Orders.API.csproj";
        var before = TypeIndex.Of([
            (ordersApp, "namespace Acme.Shop.Orders.Application.Events;\npublic class Handler { }\n"),
            (billingApp, "namespace Acme.Shop.Billing.Application.Events;\npublic class Handler { }\n"),
        ]);
        var after = TypeIndex.Of([
            (ordersDomain, "namespace Acme.Shop.Orders.Domain.Events;\npublic class Handler { }\n"),
            (billingDomain, "namespace Acme.Shop.Billing.Domain.Events;\npublic class Handler { }\n"),
        ]);
        var map = TypeMap.Between(before, after);

        var given = ReferenceRepairs.Decide(
            [new BuildError(ordersFile, "CS0234", "The type or namespace name 'Events' does not exist in the namespace 'Acme.Shop.Orders.Application'", ordersApi)],
            map, Root, _ => ["Acme.Shop.Orders.Application.Events"]);
        var type = ReferenceRepairs.Decide(
            [new BuildError(ordersFile, "CS0246", "The type or namespace name 'Handler' could not be found", ordersApi)], map, Root,
            _ => ["Acme.Shop.Orders.Application.Events"]);
        var teams = ReferenceRepairs.Decide(
            [new BuildError(ordersFile, "CS0246", "The type or namespace name 'Handler' could not be found", ordersApi)], map, Root, _ => ["Acme.Shop.Orders.Team"]);

        Repair[] expected = [new AddReference(Path.GetFullPath(ordersApi), Path.GetFullPath(Path.Combine(Root, ordersDomain)))];
        given.ShouldBe([new ReplaceUsing(ordersFile, "Acme.Shop.Orders.Application.Events", "Acme.Shop.Orders.Domain.Events"), .. expected]);
        type.ShouldBe([new AddUsing(ordersFile, "Acme.Shop.Orders.Domain.Events"), .. expected]);
        teams.ShouldBeEmpty();
    }

    [Test]
    public void AUsingOfAGivenUpNamespace_IsReplaced_AndTheProjectItsTypesWentToIsReferenced()
    {
        var repairs = ReferenceRepairs.Decide(
            [Error("CS0234", "The type or namespace name 'Events' does not exist in the namespace 'Acme.Shop.Common' (are you missing an assembly reference?)")],
            TypeMap.Between(Before, After), Root, Usings);

        repairs.ShouldBe([
            new ReplaceUsing(OrderFile, "Acme.Shop.Common.Events", "Acme.Shop.Framework.Events"),
            new AddReference(Path.GetFullPath(Orders), Path.GetFullPath(Path.Combine(Root, Framework))),
        ]);
    }

    [Test]
    public void TheCompilersFirstMissingSegment_FindsTheFilesUsingOfAGivenUpNamespace()
    {
        var repairs = ReferenceRepairs.Decide(
            [Error("CS0234", "The type or namespace name 'Common' does not exist in the namespace 'Acme.Shop' (are you missing an assembly reference?)")],
            TypeMap.Between(Before, After), Root, Usings);

        repairs.ShouldContain(new ReplaceUsing(OrderFile, "Acme.Shop.Common.Events", "Acme.Shop.Framework.Events"));
    }

    [Test]
    public void ATypeNotFound_ThatTheTemplateHasInOnePlace_GetsItsNamespaceAndProject_EvenWhenGeneric()
    {
        var repairs = ReferenceRepairs.Decide(
            [Error("CS0246", "The type or namespace name 'DomainEvent<>' could not be found (are you missing a using directive or an assembly reference?)")],
            TypeMap.Between(Before, After), Root, Usings);

        repairs.ShouldBe([
            new AddUsing(OrderFile, "Acme.Shop.Framework.Events"),
            new AddReference(Path.GetFullPath(Orders), Path.GetFullPath(Path.Combine(Root, Framework))),
        ]);
    }

    [Test]
    public void ATypeTheTemplateDidNotMove_OrMovedToTwoHomes_AndAnErrorOfAnotherKind_AreLeftToAPerson()
    {
        var before = TypeIndex.Of([(Common, "namespace A;\npublic class Clash { }\n")]);
        var after = TypeIndex.Of([(Framework, "namespace B;\npublic class Clash { }\n"), ("src/capabilities/X/X.csproj", "namespace C;\npublic class Clash { }\n")]);

        ReferenceRepairs.Decide([
            Error("CS0246", "The type or namespace name 'Clash' could not be found"),
            Error("CS0246", "The type or namespace name 'Marker' could not be found"),
            Error("CS0246", "The type or namespace name 'Unknown' could not be found"),
            Error("CS0103", "The name 'x' does not exist in the current context"),
        ], TypeMap.Between(before, after), Root, Usings).ShouldBeEmpty();
        ReferenceRepairs.Decide([Error("CS0246", "The type or namespace name 'Marker' could not be found")], TypeMap.Between(Before, After), Root, Usings)
            .ShouldBeEmpty();
    }

    [Test]
    public void AnErrorOfAProjectOfSeveralFrameworks_NamesTheProjectFile()
    {
        var error = BuildErrors.Parse("/s/A/X.cs(3,7): error CS0246: The type or namespace name 'T' could not be found [/s/A/A.csproj::TargetFramework=net8.0]\n").Single();

        error.Project.ShouldBe("/s/A/A.csproj");
    }

    private sealed class FailingWriter : IRepairWriter
    {
        public Task<int> ApplyAsync(IReadOnlyList<Repair> repairs, CancellationToken ct) =>
            throw new IOException("The file Order.cs is locked by another process.");
    }

    private sealed class NoBuilder : IBuilder
    {
        public Task<Io.ProcessResult> BuildAsync(string root, CancellationToken ct) => throw new InvalidOperationException("No build after a failed write.");
    }

    [Test]
    public async Task AWriteThatFails_StopsTheRepair_WithItsReason_AndTheBuildItHad()
    {
        // The repair reads the file's usings: Order.cs of a solution on disk.
        var root = Io.Folders.NewTemp("repair-stop");
        var file = Path.Combine(root, "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Order.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "using Acme.Shop.Common.Events;\n");
        var failed = new Io.ProcessResult(1, $"{file}(1,7): error CS0246: The type or namespace name 'DomainEvent' could not be found [{Path.Combine(root, Orders[3..])}]\n", "");

        var result = await new ReferenceRepair(new FailingWriter(), new NoBuilder()).RunAsync(root, TypeMap.Between(Before, After), failed, CancellationToken.None);

        result.ShouldBe(new RepairResult(failed, 0, "The file Order.cs is locked by another process."));
    }

    [Test]
    public void AUsing_GoesAfterTheLastUsing_OnceOnly_KeepingTheLineEndings()
    {
        var text = "using System;\r\nusing Acme.Shop.Common;\r\n\r\nnamespace X;\r\n";

        var added = UsingLines.Add(text, "Acme.Shop.Framework.Events");

        added.ShouldBe("using System;\r\nusing Acme.Shop.Common;\r\nusing Acme.Shop.Framework.Events;\r\n\r\nnamespace X;\r\n");
        UsingLines.Add(added, "Acme.Shop.Framework.Events").ShouldBe(added);
        UsingLines.Add("namespace X;\n", "N").ShouldBe("using N;\nnamespace X;\n");
    }

    [Test]
    public void TheUsingsOfAFile_AreItsNamespaces_WithoutAliasesAndStaticOnes()
    {
        UsingLines.Of("using System;\r\nusing static System.Math;\r\nusing Io = System.IO;\r\nusing Acme.Shop.Common;\r\n").ShouldBe(["System", "Acme.Shop.Common"]);
        UsingLines.Of("global using Acme.Shop.Common.Tests;\n").ShouldBe(["Acme.Shop.Common.Tests"]);
    }

    [Test]
    public void AReplacedUsing_TakesTheNewNamespaceInPlace_OrGoesWhenTheFileUsesItAlready()
    {
        UsingLines.Replace("global using Old.Ns;\n", "Old.Ns", "New.Ns").ShouldBe("global using New.Ns;\n");
        UsingLines.Replace("using Old.Ns;\nnamespace X;\n", "Old.Ns", "New.Ns").ShouldBe("using New.Ns;\nnamespace X;\n");
        UsingLines.Replace("using Old.Ns;\nusing New.Ns;\nnamespace X;\n", "Old.Ns", "New.Ns").ShouldBe("using New.Ns;\nnamespace X;\n");
        UsingLines.Replace("using New.A;\nnamespace X;\n", "Old.Ns", "New.B").ShouldBe("using New.A;\nusing New.B;\nnamespace X;\n");
    }
}
