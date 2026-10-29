using DotsKit.Io;
using DotsKit.Solutions;

namespace DotsKit.Tests.Scenarios;

/// <summary>
/// The projects of All.sln through a real <c>dotnet sln</c>, over a merge that left the files MSBuild imports in
/// conflict: the membership of the solution does not depend on them.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class SolutionProjectsScenarios
{
    private const string Conflict = "<Project>\n<<<<<<< solution\n  <PropertyGroup><A>team</A></PropertyGroup>\n=======\n  <PropertyGroup><A>template</A></PropertyGroup>\n>>>>>>> template\n</Project>\n";

    private const string EmptySolution =
        "\nMicrosoft Visual Studio Solution File, Format Version 12.00\n# Visual Studio Version 17\nVisualStudioVersion = 17.0.31903.59\nMinimumVisualStudioVersion = 10.0.40219.1\nGlobal\nEndGlobal\n";

    [Test]
    public async Task AProjectTakesThePlatformsOfTheTeamsProps_WhenTheyAreReadable()
    {
        var root = Folders.NewTemp("sln-platforms");
        var services = Path.Combine(root, "src", "services");
        var project = Path.Combine(services, "Acme.Shop.Orders", "Acme.Shop.Orders.Api", "Acme.Shop.Orders.Api.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>\n");
        await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project><PropertyGroup><Platforms>x64</Platforms></PropertyGroup></Project>\n");
        var solution = Path.Combine(services, "Acme.Shop.All.sln");
        await File.WriteAllTextAsync(solution, EmptySolution);

        await new SolutionProjects(new ProcessRunner()).AddAsync(solution, ["Acme.Shop.Orders/Acme.Shop.Orders.Api/Acme.Shop.Orders.Api.csproj"], CancellationToken.None);

        (await File.ReadAllTextAsync(solution)).ShouldContain("Debug|x64");
    }

    [Test]
    public async Task AProjectIsAddedAndRemoved_WhenTheMergeLeftDirectoryBuildPropsInConflict()
    {
        var root = Folders.NewTemp("sln-conflict");
        var services = Path.Combine(root, "src", "services");
        var project = Path.Combine(services, "Acme.Shop.Orders", "Acme.Shop.Orders.Api", "Acme.Shop.Orders.Api.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>\n");
        await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), Conflict);
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Directory.Packages.props"), Conflict);
        var solution = Path.Combine(services, "Acme.Shop.All.sln");
        await File.WriteAllTextAsync(solution, EmptySolution);
        var projects = new SolutionProjects(new ProcessRunner());
        const string listed = "Acme.Shop.Orders/Acme.Shop.Orders.Api/Acme.Shop.Orders.Api.csproj";

        await projects.AddAsync(solution, [listed], CancellationToken.None);
        var added = await projects.ListAsync(solution, CancellationToken.None);
        var text = await File.ReadAllTextAsync(solution);
        await projects.RemoveAsync(solution, [listed], CancellationToken.None);

        added.ShouldContain(listed);
        text.ShouldContain("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Acme.Shop.Orders.Api\"");
        text.ShouldContain("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Orders\"");
        (await projects.ListAsync(solution, CancellationToken.None)).ShouldBeEmpty();
    }
}
