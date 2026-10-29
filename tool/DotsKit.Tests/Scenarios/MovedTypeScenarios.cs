using DotsKit.Io;
using DotsKit.Solutions;

namespace DotsKit.Tests.Scenarios;

/// <summary>
/// A type of the template moved to another project and namespace, as 3.0 moves Common's (#101), and the team's code
/// that used it built again by the repair of dotskit upgrade (#117), with the real dotnet build.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class MovedTypeScenarios
{
    private const string Common = "src/common/Acme.Shop.Common/Acme.Shop.Common.csproj";
    private const string Framework = "src/framework/Acme.Shop.Framework.Events/Acme.Shop.Framework.Events.csproj";
    private const string Orders = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Acme.Shop.Orders.Domain.csproj";
    private const string Order = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Domain/Order.cs";
    private const string Solution = "src/services/Acme.Shop.All.sln";

    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private const string EmptySolution = "Microsoft Visual Studio Solution File, Format Version 12.00\nGlobal\nEndGlobal\n";

    private const string EventOf = "namespace {0};\n\npublic sealed class DomainEvent;\n";
    private const string Marker = "namespace Acme.Shop.Common;\n\npublic static class Marker;\n";
    private const string OrderText = "using Acme.Shop.Common.Events;\n\nnamespace Acme.Shop.Orders.Domain;\n\npublic sealed class Order\n{\n    public DomainEvent? Last { get; set; }\n}\n";

    private static readonly ProcessRunner Runner = new();

    private static void Put(string root, string path, string text)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static string Folder(string project) => Path.GetDirectoryName(project)!;

    [Test]
    public async Task TheIndexOfAGeneratedSolution_FindsATypeOfCommon_InOneHome_WithTheSolutionsNamespace()
    {
        var solution = await Sandbox.SolutionAsync();

        var homes = TypeIndex.Read(solution.Root).Find("IDomainEventHandler");

        homes.ShouldBe([new TypeHome("IDomainEventHandler", $"{Sandbox.Prefix}.Common.Application.Events.Handlers",
            $"src/common/{Sandbox.Prefix}.Common.Application/{Sandbox.Prefix}.Common.Application.csproj")]);
    }

    [Test]
    public async Task TheTeamsCode_ThatUsesAMovedType_BuildsAfterTheRepair_WithTheUsingAndTheReferenceOfItsNewHome()
    {
        var before = Folders.NewTemp("moved-base");
        Put(before, Common, Project);
        Put(before, Folder(Common) + "/DomainEvent.cs", string.Format(EventOf, "Acme.Shop.Common.Events"));
        Put(before, Folder(Common) + "/Marker.cs", Marker);

        // The solution after the merge: the template moved DomainEvent out of Common, the team's Order still uses it.
        var root = Folders.NewTemp("moved-next");
        Put(root, Common, Project);
        Put(root, Folder(Common) + "/Marker.cs", Marker);
        Put(root, Framework, Project);
        Put(root, Folder(Framework) + "/DomainEvent.cs", string.Format(EventOf, "Acme.Shop.Framework.Events"));
        Put(root, Orders, Project);
        Put(root, Order, OrderText);
        // Written by hand: dotnet new sln makes an .slnx from the SDK 10 on, and All.sln is an .sln.
        Put(root, Solution, EmptySolution);
        await Runner.RunCheckedAsync("dotnet", ["sln", Solution, "add", Common, Framework, Orders], root, CancellationToken.None);
        await Runner.RunCheckedAsync("dotnet", ["add", Orders, "reference", Common], root, CancellationToken.None);
        var builder = new DotnetBuilder(Runner);
        var failed = await builder.BuildAsync(root, CancellationToken.None);
        failed.Code.ShouldNotBe(0);

        var map = TypeMap.Between(TypeIndex.Read(before), TypeIndex.Read(root));
        var (build, repaired, stopped) = await new ReferenceRepair(new RepairWriter(Runner), builder).RunAsync(root, map, failed, CancellationToken.None);

        build.Code.ShouldBe(0, build.Output + build.Error);
        repaired.ShouldBeGreaterThan(0);
        stopped.ShouldBeNull();
        File.ReadAllText(Path.Combine(root, Order)).ShouldStartWith("using Acme.Shop.Framework.Events;\n");
        File.ReadAllText(Path.Combine(root, Orders)).ShouldContain("Acme.Shop.Framework.Events.csproj");
    }

    [Test]
    public async Task TheTeamsCode_ThatUsesAGivenUpNamespace_IsRepairedFromItsText_AndBuildsTheFirstTime()
    {
        var before = Folders.NewTemp("moved-text-base");
        Put(before, Common, Project);
        Put(before, Folder(Common) + "/DomainEvent.cs", string.Format(EventOf, "Acme.Shop.Common.Events"));
        Put(before, Folder(Common) + "/Marker.cs", Marker);
        var root = Folders.NewTemp("moved-text-next");
        Put(root, Common, Project);
        Put(root, Folder(Common) + "/Marker.cs", Marker);
        Put(root, Framework, Project);
        Put(root, Folder(Framework) + "/DomainEvent.cs", string.Format(EventOf, "Acme.Shop.Framework.Events"));
        Put(root, Orders, Project);
        Put(root, Order, OrderText);
        Put(root, Solution, EmptySolution);
        await Runner.RunCheckedAsync("dotnet", ["sln", Solution, "add", Common, Framework, Orders], root, CancellationToken.None);
        await Runner.RunCheckedAsync("dotnet", ["add", Orders, "reference", Common], root, CancellationToken.None);
        var map = TypeMap.Between(TypeIndex.Read(before), TypeIndex.Read(root));

        var repaired = await new ReferenceRepair(new RepairWriter(Runner), new DotnetBuilder(Runner)).BeforeBuildAsync(root, map, [], CancellationToken.None);
        var build = await new DotnetBuilder(Runner).BuildAsync(root, CancellationToken.None);

        repaired.Count.ShouldBe(2);
        build.Code.ShouldBe(0, build.Output + build.Error);
    }
}
