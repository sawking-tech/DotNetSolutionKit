using DotsKit.Io;
using DotsKit.PackageUpdates;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>A solution on central package management: a version a project still names goes to the central file.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class CentralVersionsTests
{
    private const string Central = "src/Directory.Packages.props";
    private const string Billing = "src/services/Acme.Shop.Billing/Acme.Shop.Billing.Application/Acme.Shop.Billing.Application.csproj";
    private const string Orders = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Application/Acme.Shop.Orders.Application.csproj";
    private const string Left = "src/services/Acme.Shop.Stock/Acme.Shop.Stock.Application/Acme.Shop.Stock.Application.csproj";
    private const string Managed = "<Project>\n  <PropertyGroup>\n    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>\n  </PropertyGroup>\n";

    private static void Put(string root, string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
        File.WriteAllText(Path.Combine(root, path), text);
    }

    private static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path));

    private static string References(params string[] items) =>
        "<Project>\n  <ItemGroup>\n" + string.Concat(items.Select(i => $"    {i}\n")) + "  </ItemGroup>\n</Project>\n";

    [Test]
    public void AVersionAProjectNames_GoesToTheCentralFile_AnotherOneThereStaysAsAnOverride_AndAProjectInConflictWaits()
    {
        var root = Folders.NewTemp("central");
        Put(root, Central, Managed + "  <ItemGroup>\n    <PackageVersion Include=\"Npgsql\" Version=\"8.0.4\" />\n  </ItemGroup>\n</Project>\n");
        Put(root, Billing, References("<PackageReference Include=\"Scriban\" Version=\"5.10.0\" />", "<PackageReference Include=\"Npgsql\" Version=\"7.0.0\" />"));
        Put(root, Orders, References("<PackageReference Include=\"Scriban\" Version=\"5.12.0\" PrivateAssets=\"all\" />"));
        Put(root, Left, References("<PackageReference Include=\"Polly\" Version=\"8.0.0\" />"));

        var repairs = CentralVersions.Decide(root, new HashSet<string> { Left });
        foreach (var repair in repairs.Cast<CentralizeVersion>())
        {
            CentralVersions.Apply(repair);
        }

        repairs.Count.ShouldBe(3);
        PackageVersions.Read(Read(root, Central)).ShouldBe(new Dictionary<string, string> { ["Npgsql"] = "8.0.4", ["Scriban"] = "5.10.0" });
        // No package changes its version: one the central file has otherwise stays the project's override.
        Read(root, Billing).ShouldContain("<PackageReference Include=\"Scriban\" />");
        Read(root, Billing).ShouldContain("<PackageReference Include=\"Npgsql\" VersionOverride=\"7.0.0\" />");
        Read(root, Orders).ShouldContain("<PackageReference Include=\"Scriban\" VersionOverride=\"5.12.0\" PrivateAssets=\"all\" />");
        Read(root, Left).ShouldContain("Version=\"8.0.0\"");
    }

    [Test]
    public void ACentralFileInConflict_AProjectOutsideSrc_AndAProjectThatOptsOut_AreLeftAsTheyAre()
    {
        var root = Folders.NewTemp("central-left");
        Put(root, Central, Managed + "  <ItemGroup>\n  </ItemGroup>\n</Project>\n");
        Put(root, "tools/Migrator/Migrator.csproj", References("<PackageReference Include=\"Dapper\" Version=\"2.1.0\" />"));
        Put(root, Billing, "<Project>\n  <PropertyGroup>\n    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>\n  </PropertyGroup>\n"
            + "  <ItemGroup>\n    <PackageReference Include=\"Scriban\" Version=\"5.10.0\" />\n  </ItemGroup>\n</Project>\n");
        Put(root, Orders, References("<PackageReference Include=\"Polly\" Version=\"8.0.0\" />"));

        CentralVersions.Decide(root, new HashSet<string>()).Cast<CentralizeVersion>().Select(r => r.Package).ShouldBe(["Polly"]);
        CentralVersions.Decide(root, new HashSet<string> { Central }).ShouldBeEmpty();
    }

    [Test]
    public void AVersionAFlagsFileDeclares_IsNotDeclaredAgain_AndADifferentOneStaysAsAnOverride()
    {
        var root = Folders.NewTemp("central-flag");
        Put(root, Central, Managed + "  <ItemGroup>\n  </ItemGroup>\n  <Import Project=\"package-versions/postgres.props\" />\n</Project>\n");
        Put(root, "src/package-versions/postgres.props", "<Project>\n  <ItemGroup>\n    <PackageVersion Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" Version=\"8.0.11\" />\n  </ItemGroup>\n</Project>\n");
        Put(root, Billing, References("<PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" Version=\"8.0.11\" />"));
        Put(root, Orders, References("<PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" Version=\"8.0.4\" />"));

        foreach (var repair in CentralVersions.Decide(root, new HashSet<string>()).Cast<CentralizeVersion>())
        {
            CentralVersions.Apply(repair);
        }

        Read(root, Central).ShouldNotContain("Npgsql", Case.Insensitive);
        Read(root, Billing).ShouldContain("<PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" />");
        Read(root, Orders).ShouldContain("<PackageReference Include=\"Npgsql.EntityFrameworkCore.PostgreSQL\" VersionOverride=\"8.0.4\" />");
    }

    [Test]
    public void AFolderThatOptsOutInItsBuildProps_AndAProjectOfAPartNotMergedYet_AreLeftAsTheyAre()
    {
        var root = Folders.NewTemp("central-props");
        Put(root, Central, Managed + "  <ItemGroup>\n  </ItemGroup>\n</Project>\n");
        Put(root, "src/legacy/Directory.Build.props", "<Project>\n  <PropertyGroup>\n    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>\n  </PropertyGroup>\n</Project>\n");
        Put(root, "src/legacy/Old/Old.csproj", References("<PackageReference Include=\"Dapper\" Version=\"1.50.0\" />"));
        Put(root, Billing, References("<PackageReference Include=\"Scriban\" Version=\"5.10.0\" />"));
        Put(root, Orders, References("<PackageReference Include=\"Polly\" Version=\"8.0.0\" />"));

        CentralVersions.Decide(root, new HashSet<string>(), new HashSet<string> { Billing }).Cast<CentralizeVersion>().Select(r => r.Package).ShouldBe(["Polly"]);
    }

    [Test]
    public void ASolutionThatDoesNotManageItsVersionsCentrally_IsLeftAsItIs()
    {
        var root = Folders.NewTemp("not-central");
        Put(root, Billing, References("<PackageReference Include=\"Scriban\" Version=\"5.10.0\" />"));

        CentralVersions.Decide(root, new HashSet<string>()).ShouldBeEmpty();
    }
}
