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
    private const string Common = "src/common/ST.Demo.Common/ST.Demo.Common.csproj";
    private const string Framework = "src/framework/ST.Demo.Framework.Events/ST.Demo.Framework.Events.csproj";
    private const string Orders = "/s/src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/ST.Demo.Orders.Domain.csproj";
    private const string OrderFile = "/s/src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Order.cs";

    private static readonly TypeIndex Before = TypeIndex.Of([
        (Common, "namespace ST.Demo.Common.Events;\n\npublic sealed class DomainEvent;\n"),
        (Common, "namespace ST.Demo.Common\n{\n    public static class Marker { }\n}\n"),
    ]);

    private static readonly TypeIndex After = TypeIndex.Of([
        (Framework, "namespace ST.Demo.Framework.Events;\n\npublic sealed class DomainEvent;\n"),
        (Common, "namespace ST.Demo.Common\n{\n    public static class Marker { }\n}\n"),
    ]);

    private static BuildError Error(string code, string message) => new(OrderFile, code, message, Orders);

    /// <summary>Order.cs as the team wrote it, with the using of the namespace the template gave up.</summary>
    private static string Text(string file) =>
        file == OrderFile ? "using System;\nusing ST.Demo.Common.Events;\n\nnamespace ST.Demo.Orders.Domain;\n\npublic sealed class Order\n{\n    public DomainEvent? Last { get; set; }\n}\n" : "";

    [Test]
    public void TheIndex_HoldsThePublicTypes_WithTheirProjectAndNamespace_AndLeavesOutTheInternalOnes()
    {
        var index = TypeIndex.Of([(Common, "namespace ST.Demo.Common;\n\npublic sealed record Money(decimal Amount);\npublic interface IClock { }\ninternal sealed class Hidden { }\n")]);

        index.Find("Money").ShouldBe([new TypeHome("Money", "ST.Demo.Common", Common)]);
        index.Find("IClock").ShouldHaveSingleItem();
        index.Find("Hidden").ShouldBeEmpty();
    }

    [Test]
    public void AMovedType_IsFollowedToItsNewHome_AndAKeptOneIsNoMove()
    {
        var map = TypeMap.Between(Before, After);

        map.Moves.ShouldBe([new TypeMove(
            new TypeHome("DomainEvent", "ST.Demo.Common.Events", Common),
            new TypeHome("DomainEvent", "ST.Demo.Framework.Events", Framework))]);
        map.OutOf("ST.Demo.Common.Events").ShouldHaveSingleItem();
        map.OutOf("ST.Demo.Common").ShouldBeEmpty();
    }

    [Test]
    public void AServicesCopyOfAMovedType_LeadsToItsOwnServiceOnly()
    {
        const string ordersApp = "src/services/ST.Demo.Orders/ST.Demo.Orders.Application/ST.Demo.Orders.Application.csproj";
        const string ordersDomain = "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/ST.Demo.Orders.Domain.csproj";
        const string billingApp = "src/services/ST.Demo.Billing/ST.Demo.Billing.Application/ST.Demo.Billing.Application.csproj";
        const string billingDomain = "src/services/ST.Demo.Billing/ST.Demo.Billing.Domain/ST.Demo.Billing.Domain.csproj";
        const string ordersFile = "/s/src/services/ST.Demo.Orders/ST.Demo.Orders.API/Endpoint.cs";
        const string ordersApi = "/s/src/services/ST.Demo.Orders/ST.Demo.Orders.API/ST.Demo.Orders.API.csproj";
        var before = TypeIndex.Of([
            (ordersApp, "namespace ST.Demo.Orders.Application.Events;\npublic class Handler { }\n"),
            (billingApp, "namespace ST.Demo.Billing.Application.Events;\npublic class Handler { }\n"),
        ]);
        var after = TypeIndex.Of([
            (ordersDomain, "namespace ST.Demo.Orders.Domain.Events;\npublic class Handler { }\n"),
            (billingDomain, "namespace ST.Demo.Billing.Domain.Events;\npublic class Handler { }\n"),
        ]);
        var map = TypeMap.Between(before, after);

        const string endpoint = "using ST.Demo.Orders.Application.Events;\n\npublic class Endpoint\n{\n    public Handler? H { get; set; }\n}\n";
        var given = ReferenceRepairs.Decide(
            [new BuildError(ordersFile, "CS0234", "The type or namespace name 'Events' does not exist in the namespace 'ST.Demo.Orders.Application'", ordersApi)],
            map, Root, _ => endpoint);
        var type = ReferenceRepairs.Decide(
            [new BuildError(ordersFile, "CS0246", "The type or namespace name 'Handler' could not be found", ordersApi)], map, Root, _ => endpoint);
        var teams = ReferenceRepairs.Decide(
            [new BuildError(ordersFile, "CS0246", "The type or namespace name 'Handler' could not be found", ordersApi)], map, Root,
            _ => "using ST.Demo.Orders.Team;\n\npublic class Endpoint\n{\n    public Handler? H { get; set; }\n}\n");

        Repair[] expected = [new AddReference(Path.GetFullPath(ordersApi), Path.GetFullPath(Path.Combine(Root, ordersDomain)))];
        given.ShouldBe([new ReplaceUsing(ordersFile, "ST.Demo.Orders.Application.Events", "ST.Demo.Orders.Domain.Events"), .. expected]);
        type.ShouldBe([new AddUsing(ordersFile, "ST.Demo.Orders.Domain.Events"), .. expected]);
        teams.ShouldBeEmpty();
    }

    [Test]
    public void AnErrorInAnotherLocale_IsReadByItsQuotedNames()
    {
        // MSBuild quotes with double quotes and writes the message in the machine's locale, here Russian.
        // The file declares the type's old namespace itself - the reference needs no using - as the same-namespace
        // files a move breaks: PostgresDbTestExecutionContext and the moved ServiceDbTestExecutionContext.
        var localized = ReferenceRepairs.Decide(
            [new BuildError(OrderFile, "CS0246", "Не удалось найти тип или имя пространства имен \"DomainEvent\" (возможно, отсутствует директива using или ссылка на сборку).", Orders)],
            Split, Root, _ => "namespace ST.Demo.Common.Events;\npublic class Order\n{\n}\n");

        localized.ShouldBe([new AddUsing(OrderFile, "ST.Demo.Common.Domain.Events"),
            new AddReference(Path.GetFullPath(Orders), Path.GetFullPath(Path.Combine(Root, CommonDomain)))]);
    }

    [Test]
    public void AnExtensionMethodThatMoved_IsFollowedByItsName()
    {
        // MSBuild names the receiver first, the method second, in the machine's locale.
        var extensions = TypeMap.Between(
            TypeIndex.Of([(Common, "namespace ST.Demo.Common.Tests;\npublic static class Setup\n{\n    public static IServiceCollection AddDomainEvents(this IServiceCollection services) => services;\n}\n")]),
            TypeIndex.Of([(Framework, "namespace ST.Demo.Framework.Testing;\npublic static class Setup\n{\n    public static IServiceCollection AddDomainEvents(this IServiceCollection services) => services;\n}\n")]));
        var services = Path.Combine(Root, "src/services/ST.Demo.Orders/ST.Demo.Orders.API/ST.Demo.Orders.API.csproj");

        var repairs = ReferenceRepairs.Decide(
            [new BuildError(OrderFile, "CS1061", "\"IServiceCollection\" не содержит определения \"AddDomainEvents\"", services)],
            extensions, Root, _ => "namespace ST.Demo.Common.Tests.Integration;\npublic class Endpoint\n{\n}\n");

        repairs.ShouldBe([new AddUsing(OrderFile, "ST.Demo.Framework.Testing"),
            new AddReference(Path.GetFullPath(services), Path.GetFullPath(Path.Combine(Root, Framework)))]);
    }

    [Test]
    public void AUsingOfAGivenUpNamespace_IsReplaced_AndTheProjectItsTypesWentToIsReferenced()
    {
        var repairs = ReferenceRepairs.Decide(
            [Error("CS0234", "The type or namespace name 'Events' does not exist in the namespace 'ST.Demo.Common' (are you missing an assembly reference?)")],
            TypeMap.Between(Before, After), Root, Text);

        repairs.ShouldBe([
            new ReplaceUsing(OrderFile, "ST.Demo.Common.Events", "ST.Demo.Framework.Events"),
            new AddReference(Path.GetFullPath(Orders), Path.GetFullPath(Path.Combine(Root, Framework))),
        ]);
    }

    [Test]
    public void TheCompilersFirstMissingSegment_FindsTheFilesUsingOfAGivenUpNamespace()
    {
        var repairs = ReferenceRepairs.Decide(
            [Error("CS0234", "The type or namespace name 'Common' does not exist in the namespace 'ST.Demo' (are you missing an assembly reference?)")],
            TypeMap.Between(Before, After), Root, Text);

        repairs.ShouldContain(new ReplaceUsing(OrderFile, "ST.Demo.Common.Events", "ST.Demo.Framework.Events"));
    }

    [Test]
    public void AUsingOfAGivenUpNamespace_IsRepairedFromTheText_BeforeAnyBuild_AndAKeptOneIsLeft()
    {
        var files = new[]
        {
            new SourceFile(OrderFile, Orders, ["System", "ST.Demo.Common.Events", "ST.Demo.Common"], ["ST.Demo.Orders.Domain"]),
            new SourceFile("/s/src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Other.cs", Orders, ["ST.Demo.Common"], []),
        };

        ReferenceRepairs.FromText(files, TypeMap.Between(Before, After), Root).ShouldBe([
            new ReplaceUsing(OrderFile, "ST.Demo.Common.Events", "ST.Demo.Framework.Events"),
            new AddReference(Path.GetFullPath(Orders), Path.GetFullPath(Path.Combine(Root, Framework))),
        ]);
    }

    private const string CommonDomain = "src/common/ST.Demo.Common.Domain/ST.Demo.Common.Domain.csproj";
    private const string CommonInfrastructure = "src/common/ST.Demo.Common.Infrastructure/ST.Demo.Common.Infrastructure.csproj";

    /// <summary>2.x Common.Events holds two types; the next version takes them apart, to the domain and the infrastructure.</summary>
    private static readonly TypeMap Split = TypeMap.Between(
        TypeIndex.Of([
            (Common, "namespace ST.Demo.Common.Events;\n\npublic sealed class DomainEvent;\npublic static class EventDispatcher { }\n"),
        ]),
        TypeIndex.Of([
            (CommonDomain, "namespace ST.Demo.Common.Domain.Events;\n\npublic sealed class DomainEvent;\n"),
            (CommonInfrastructure, "namespace ST.Demo.Common.Infrastructure.Events;\n\npublic static class EventDispatcher { }\n"),
        ]));

    [Test]
    public void AFileGetsTheDestinationsOfTheTypesItNames_NotEveryDestinationOfItsUsing()
    {
        var files = new[]
        {
            new SourceFile(OrderFile, Orders, ["ST.Demo.Common.Events"], ["ST.Demo.Orders.Domain"],
                "using ST.Demo.Common.Events;\n\nnamespace ST.Demo.Orders.Domain;\n\npublic sealed class Order\n{\n    public DomainEvent? Last { get; set; }\n    // EventDispatcher in a comment does not count\n    public string Kind = \"EventDispatcher\";\n}\n"),
        };

        ReferenceRepairs.FromText(files, Split, Root).ShouldBe([
            new ReplaceUsing(OrderFile, "ST.Demo.Common.Events", "ST.Demo.Common.Domain.Events"),
            new AddReference(Path.GetFullPath(Orders), Path.GetFullPath(Path.Combine(Root, CommonDomain))),
        ]);
    }

    [Test]
    public void AFileThatNamesNoMovedType_AsOneUsingExtensionMethodsOnly_GetsEveryDestination()
    {
        var files = new[]
        {
            new SourceFile(OrderFile, Orders, ["ST.Demo.Common.Events"], ["ST.Demo.Orders.Domain"],
                "using ST.Demo.Common.Events;\n\nnamespace ST.Demo.Orders.Domain;\n\npublic sealed class Order\n{\n    public void Raise(object e) => e.Dispatch();\n}\n"),
        };

        var repairs = ReferenceRepairs.FromText(files, Split, Root);

        repairs.OfType<ReplaceUsing>().Select(r => r.With).ShouldBe(["ST.Demo.Common.Domain.Events", "ST.Demo.Common.Infrastructure.Events"], ignoreOrder: true);
        repairs.OfType<AddReference>().Count().ShouldBe(2);
    }

    [Test]
    public void ATypeDeleted_BesideANamesakeTheBaseAlreadyHad_IsNoMove()
    {
        var before = TypeIndex.Of([
            (Common, "namespace ST.Demo.Common.Events;\n\npublic sealed class Marker;\n"),
            (CommonDomain, "namespace ST.Demo.Common.Domain;\n\npublic sealed class Marker;\n"),
        ]);
        var after = TypeIndex.Of([
            (CommonDomain, "namespace ST.Demo.Common.Domain;\n\npublic sealed class Marker;\n"),
        ]);

        TypeMap.Between(before, after).Moves.ShouldBeEmpty();
    }

    [Test]
    public void AGivenUpNamespace_ThatTheTeamsCodeStillDeclares_IsLeftToTheBuild()
    {
        var files = new[]
        {
            new SourceFile(OrderFile, Orders, ["ST.Demo.Common.Events"], []),
            new SourceFile("/s/src/common/ST.Demo.Common/Events/OrderShipped.cs", "/s/" + Common, [], ["ST.Demo.Common.Events"]),
        };

        ReferenceRepairs.FromText(files, TypeMap.Between(Before, After), Root).ShouldBeEmpty();
    }

    [Test]
    public void TheNamespacesAFileDeclares_FileScopedOrInABlock_AreRead()
    {
        UsingLines.Declared("using System;\r\n\r\nnamespace A.B;\r\n").ShouldBe(["A.B"]);
        UsingLines.Declared("namespace C\n{\n    namespace D { }\n}\n").ShouldBe(["C"]);
    }

    private sealed class RecordingWriter : IRepairWriter
    {
        public List<Repair> Repairs { get; } = [];

        public Task<IReadOnlyList<Repair>> ApplyAsync(IReadOnlyList<Repair> repairs, CancellationToken ct)
        {
            Repairs.AddRange(repairs);
            return Task.FromResult<IReadOnlyList<Repair>>([]);
        }
    }

    [Test]
    public async Task BeforeTheBuild_AFileInConflict_AFileOfAProjectInConflict_AndAFolderOfTwoProjects_AreLeftAlone()
    {
        var root = Io.Folders.NewTemp("repair-before");
        const string given = "using ST.Demo.Common.Events;\n";
        string Put(string path, string text)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
            return Path.GetFullPath(full);
        }
        Put(Orders[3..], "<Project />");
        Put("src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Order.cs", given);
        var free = Put("src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Free.cs", given);
        Put("src/services/ST.Demo.Orders/ST.Demo.Orders.Api/ST.Demo.Orders.Api.csproj", "<Project />");
        Put("src/services/ST.Demo.Orders/ST.Demo.Orders.Api/Api.cs", given);
        Put("src/services/ST.Demo.Orders/ST.Demo.Orders.Twin/A.csproj", "<Project />");
        Put("src/services/ST.Demo.Orders/ST.Demo.Orders.Twin/B.csproj", "<Project />");
        Put("src/services/ST.Demo.Orders/ST.Demo.Orders.Twin/Twin.cs", given);
        var writer = new RecordingWriter();

        await new ReferenceRepair(writer, new NoBuilder()).BeforeBuildAsync(root, TypeMap.Between(Before, After),
            ["src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Order.cs", "src/services/ST.Demo.Orders/ST.Demo.Orders.Api/ST.Demo.Orders.Api.csproj"],
            CancellationToken.None);

        writer.Repairs.OfType<ReplaceUsing>().Select(r => r.File).ShouldBe([free]);
    }

    [Test]
    public void ATypeNotFound_ThatTheTemplateHasInOnePlace_GetsItsNamespaceAndProject_EvenWhenGeneric()
    {
        var repairs = ReferenceRepairs.Decide(
            [Error("CS0246", "The type or namespace name 'DomainEvent<>' could not be found (are you missing a using directive or an assembly reference?)")],
            TypeMap.Between(Before, After), Root, Text);

        repairs.ShouldBe([
            new AddUsing(OrderFile, "ST.Demo.Framework.Events"),
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
        ], TypeMap.Between(before, after), Root, Text).ShouldBeEmpty();
        ReferenceRepairs.Decide([Error("CS0246", "The type or namespace name 'Marker' could not be found")], TypeMap.Between(Before, After), Root, Text)
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
        public Task<IReadOnlyList<Repair>> ApplyAsync(IReadOnlyList<Repair> repairs, CancellationToken ct) =>
            throw new IOException("The file Order.cs is locked by another process.");
    }

    private sealed class NoBuilder : IBuilder
    {
        public Task<Io.ProcessResult> BuildAsync(string root, CancellationToken ct) => throw new InvalidOperationException("No build after a failed write.");
    }

    private sealed class Builds : IBuilder
    {
        public Task<Io.ProcessResult> BuildAsync(string root, CancellationToken ct) => Task.FromResult(new Io.ProcessResult(0, "", ""));
    }

    [Test]
    public async Task TheTemplatesFilesOfAPartNotMergedYet_AreRepairedToo_WhenTheBuildNamesThem_TheTypeIsGoneFromItsOldHome()
    {
        var root = Io.Folders.NewTemp("repair-waiting");
        var templates = Path.Combine(root, "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Order.cs");
        var teams = Path.Combine(root, "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Refund.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(templates)!);
        File.WriteAllText(templates, "using ST.Demo.Common.Events;\n");
        File.WriteAllText(teams, "using ST.Demo.Common.Events;\n");
        var project = Path.Combine(root, Orders[3..]);
        var failed = new Io.ProcessResult(1,
            $"{templates}(1,7): error CS0246: The type or namespace name 'DomainEvent' could not be found [{project}]\n"
            + $"{teams}(1,7): error CS0246: The type or namespace name 'DomainEvent' could not be found [{project}]\n", "");
        var writer = new RecordingWriter();

        // An error of a file of a part not merged yet means the type's new home is merged already: the repair
        // retargets the file, and the merge of its own part brings both sides in line with it.
        await new ReferenceRepair(writer, new Builds()).RunAsync(root, TypeMap.Between(Before, After), failed, CancellationToken.None);

        var files = writer.Repairs.Select(RepairReport.FileOf).ToList();
        files.ShouldContain(teams);
        files.ShouldContain(templates);
    }

    [Test]
    public async Task AWriteThatFails_StopsTheRepair_WithItsReason_AndTheBuildItHad()
    {
        // The repair reads the file's usings: Order.cs of a solution on disk.
        var root = Io.Folders.NewTemp("repair-stop");
        var file = Path.Combine(root, "src/services/ST.Demo.Orders/ST.Demo.Orders.Domain/Order.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "using ST.Demo.Common.Events;\n");
        var failed = new Io.ProcessResult(1, $"{file}(1,7): error CS0246: The type or namespace name 'DomainEvent' could not be found [{Path.Combine(root, Orders[3..])}]\n", "");

        var result = await new ReferenceRepair(new FailingWriter(), new NoBuilder()).RunAsync(root, TypeMap.Between(Before, After), failed, CancellationToken.None);

        result.Build.ShouldBe(failed);
        result.Applied.ShouldBeEmpty();
        result.Stopped.ShouldBe("The file Order.cs is locked by another process.");
    }

    [Test]
    public void AUsing_GoesAfterTheLastUsing_OnceOnly_KeepingTheLineEndings()
    {
        var text = "using System;\r\nusing ST.Demo.Common;\r\n\r\nnamespace X;\r\n";

        var added = UsingLines.Add(text, "ST.Demo.Framework.Events");

        added.ShouldBe("using System;\r\nusing ST.Demo.Common;\r\nusing ST.Demo.Framework.Events;\r\n\r\nnamespace X;\r\n");
        UsingLines.Add(added, "ST.Demo.Framework.Events").ShouldBe(added);
        UsingLines.Add("namespace X;\n", "N").ShouldBe("using N;\n\nnamespace X;\n");
    }

    [Test]
    public void TheUsingsOfAFile_AreItsNamespaces_WithoutAliasesAndStaticOnes()
    {
        UsingLines.Of("using System;\r\nusing static System.Math;\r\nusing Io = System.IO;\r\nusing ST.Demo.Common;\r\n").ShouldBe(["System", "ST.Demo.Common"]);
        UsingLines.Of("global using ST.Demo.Common.Tests;\n").ShouldBe(["ST.Demo.Common.Tests"]);
    }

    [Test]
    public void AReplacedUsing_TakesTheNewNamespaceInPlace_OrGoesWhenTheFileUsesItAlready()
    {
        UsingLines.Replace("global using Old.Ns;\n", "Old.Ns", "New.Ns").ShouldBe("global using New.Ns;\n");
        UsingLines.Replace("using Old.Ns;\nnamespace X;\n", "Old.Ns", "New.Ns").ShouldBe("using New.Ns;\nnamespace X;\n");
        UsingLines.Replace("using Old.Ns;\nusing New.Ns;\nnamespace X;\n", "Old.Ns", "New.Ns").ShouldBe("using New.Ns;\nnamespace X;\n");
        UsingLines.Replace("using New.A;\nnamespace X;\n", "Old.Ns", "New.B").ShouldBe("using New.A;\nusing New.B;\n\nnamespace X;\n");
    }
}

