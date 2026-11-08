using DotsKit.Generation;
using DotsKit.Io;

namespace DotsKit.Tests;

/// <summary>Where the package of a published version comes from: nuget.org by default, the folder of DOTSKIT_TEMPLATE_PACKAGES when set.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class TemplatePackagesTests
{
    private const string PackageId = "SawKing.DotNetSolutionKit";

    [Test]
    public void ANoFolder_GivesTheNuGetForm() =>
        TemplatePackages.PackageOrNuGet(null, PackageId, "2.8.0").ShouldBe($"{PackageId}::2.8.0");

    [Test]
    public void AFolderWithThePackage_GivesTheNupkg()
    {
        var folder = Folders.NewTemp("packages");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{PackageId}.2.8.0.nupkg"), "");

        TemplatePackages.PackageOrNuGet(folder, PackageId, "2.8.0").ShouldBe(Path.Combine(folder, $"{PackageId}.2.8.0.nupkg"));
    }

    [Test]
    public void AFolderWithoutThePackage_IsNamedWithTheRemedy()
    {
        var folder = Folders.NewTemp("packages");
        Directory.CreateDirectory(folder);

        var error = Should.Throw<InvalidOperationException>(() => TemplatePackages.PackageOrNuGet(folder, PackageId, "2.8.0"));
        error.Message.ShouldContain("SawKing.DotNetSolutionKit.2.8.0.nupkg is not in DOTSKIT_TEMPLATE_PACKAGES");
        error.Message.ShouldContain("dotnet pack");
        error.Message.ShouldContain("nuget.org");
    }
}
