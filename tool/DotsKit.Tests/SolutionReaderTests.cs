using DotsKit.Reading;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>dotnet sln rewrites All.sln and silently drops a Project line that misses its EndProject: the tool refuses first.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class SolutionTextTests
{
    private const string Declaration = "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"A\\A.csproj\", \"{30C2C50E-CE11-4405-9593-189C878FD099}\"";

    [Test]
    public void APairedSolution_IsTakenAsItIs() =>
        SolutionProjects.WhyMalformed($"\nMicrosoft Visual Studio Solution File, Format Version 12.00\n{Declaration}\nEndProject\n{Declaration}\nEndProject\nGlobal\nEndGlobal\n")
            .ShouldBeNull();

    [Test]
    public void AProjectWithoutItsEndProject_IsNamedWithItsLine()
    {
        SolutionProjects.WhyMalformed($"\nheader\n{Declaration}\n{Declaration}\nEndProject\nGlobal\nEndGlobal\n")
            .ShouldBe("is malformed: the Project of line 3 has no EndProject before the Project of line 4");
        SolutionProjects.WhyMalformed($"{Declaration}\n").ShouldBe("is malformed: the Project of line 1 has no EndProject");
        SolutionProjects.WhyMalformed("header\nEndProject\n").ShouldBe("is malformed: the EndProject of line 2 closes no Project");
    }
}

/// <summary>How a solution made without dotskit is read into a manifest: files in memory, no template.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class SolutionReaderTests
{
    private const string Prefix = "Acme.Shop";
    private const string Orders = "Acme.Shop.Orders";

    /// <summary>A solution as a set of files: a path with null text is a folder or a file whose text is not read.</summary>
    private sealed class Files : ISolutionFiles
    {
        private readonly Dictionary<string, string?> _files = new(StringComparer.Ordinal);

        public Files With(string path, string? text = null)
        {
            _files[path] = text;
            return this;
        }

        /// <summary>A service the template generated: its API project, its port, its appsettings.json sections.</summary>
        public Files WithService(string folder, int port, params string[] sections) =>
            With($"src/services/{folder}/{folder}.API/{folder}.API.csproj", "<Project />")
                .With($"src/services/{folder}/{folder}.API/Properties/launchSettings.json", $"\"applicationUrl\": \"http://localhost:{port}\"")
                .With($"src/services/{folder}/{folder}.API/appsettings.json", "{" + string.Join(",", sections.Select(s => $"\"{s}\": {{}}")) + "}");

        public bool Exists(string path) =>
            path.EndsWith('*')
                ? _files.Keys.Any(k => k.StartsWith(path[..^1], StringComparison.Ordinal))
                : _files.Keys.Any(k => k == path || k.StartsWith(path + "/", StringComparison.Ordinal));

        public string? Read(string path) => _files.GetValueOrDefault(path);

        public IReadOnlyList<string> Folders(string path) =>
            [.. _files.Keys.Where(k => k.StartsWith(path + "/", StringComparison.Ordinal))
                .Select(k => k[(path.Length + 1)..].Split('/'))
                .Where(parts => parts.Length > 1)
                .Select(parts => parts[0])
                .Distinct()
                .Order(StringComparer.Ordinal)];
    }

    private static Files Solution() => new Files().WithService(Orders, 5000, "HangfireSettings");

    private static Manifest Read(Files files, params string[] given) =>
        SolutionReader.Read(files, Prefix, "2.8.0", TemplateArgs.Parse(given));

    [Test]
    public void ADefaultSolution_IsReadWithTheNamesAndTheServiceAlone()
    {
        var manifest = Read(Solution().With("deploy/compose/.env.example", "HANGFIRE_DASHBOARD_PASSWORD=").With(".claude/rules.md"));

        manifest.Solution.ShouldBe(["--NamespaceRoot", "Acme", "--ProductName", "Shop"]);
        manifest.Services.ShouldBe([["--ServiceNameOrCustom", "Orders", "--HttpPort", "5000"]]);
    }

    [Test]
    public void TheNamesOfADottedProduct_AreToldApartByTheImageNames()
    {
        var files = new Files().WithService("Acme.Corp.Retail.Shop.Orders", 5000, "HangfireSettings")
            .With("deploy/build-images.sh", "        name=\"retail_shop-$(echo \"$service\" | tr)\"");

        var manifest = SolutionReader.Read(files, "Acme.Corp.Retail.Shop", "2.8.0", TemplateArgs.Empty);

        manifest.SolutionArgs["NamespaceRoot"].ShouldBe("Acme.Corp");
        manifest.SolutionArgs["ProductName"].ShouldBe("Retail.Shop");
    }

    [Test]
    public void AProductWithAnUnderscore_IsToldApartByTheWholeImageName()
    {
        var files = new Files().WithService("Acme.Corp.Retail_Shop.Orders", 5000, "HangfireSettings")
            .With("deploy/build-images.sh", "        name=\"retail_shop-$(echo \"$service\" | tr)\"");

        var manifest = SolutionReader.Read(files, "Acme.Corp.Retail_Shop", "2.8.0", TemplateArgs.Empty);

        manifest.SolutionArgs["NamespaceRoot"].ShouldBe("Acme.Corp");
        manifest.SolutionArgs["ProductName"].ShouldBe("Retail_Shop");
    }

    [Test]
    public void TheFirstServiceOfA27Solution_IsTheOneItsScriptNames_NotOneItsNameStartsWith()
    {
        var files = Solution().WithService("Acme.Shop.OrdersArchive", 5001, "HangfireSettings")
            .With("src/services/manual-add-projects.sh", "    full_folder=$(basename \"$service_dir\")    # Acme.Shop.OrdersArchive\n");

        Read(files).ServiceArgs[0].Service.ShouldBe("OrdersArchive");
    }

    [Test]
    public void NamesNothingTellsApart_AreAskedFor()
    {
        var files = new Files().WithService("Acme.Corp.Shop.Orders", 5000);

        Should.Throw<ArgumentException>(() => SolutionReader.Read(files, "Acme.Corp.Shop", "2.8.0", TemplateArgs.Empty))
            .Message.ShouldContain("-N <NamespaceRoot> -P <ProductName>");
        SolutionReader.Read(files, "Acme.Corp.Shop", "2.8.0", TemplateArgs.Parse(["-N", "Acme", "-P", "Corp.Shop"]))
            .SolutionArgs["ProductName"].ShouldBe("Corp.Shop");
    }

    [Test]
    public void NamesThatDoNotMakeThePrefix_AreRefused()
    {
        Should.Throw<ArgumentException>(() => Read(Solution(), "-N", "Acme", "-P", "Store")).Message.ShouldContain("Acme.Shop");
    }

    [Test]
    public void AServiceFlag_IsReadFromItsSectionOfAppSettings_AndTheSolutionsFromCommon()
    {
        var files = Solution().WithService("Acme.Shop.Billing", 5001, "HangfireSettings", "S3", "MongoDB", "Email")
            .With("src/common/Acme.Shop.Common.Infrastructure/Storage/DependencyInjection.cs")
            .With("src/capabilities/Acme.Shop.Capabilities.Mongo/Mongo.csproj")
            .With("src/capabilities/Acme.Shop.Capabilities.Notifications/Notifications.csproj");

        var billing = Read(files).FindService("Billing")!;

        billing.IsOn("Storage").ShouldBeTrue();
        billing.IsOn("MongoDB").ShouldBeTrue();
        billing["Notify"].ShouldBe("email");
        Read(files).FindService("Orders")!.IsOn("MongoDB").ShouldBeFalse();
        Read(files).SolutionArgs.IsOn("MongoDB").ShouldBeTrue();
    }

    [Test]
    public void TheBus_IsTheOutboxWhereTheServiceNeedsTheDatabaseForIt_AndDirectOtherwise()
    {
        var files = Solution()
            .WithService("Acme.Shop.Billing", 5001, "HangfireSettings", "RabbitMq")
            .With("src/services/Acme.Shop.Billing/Acme.Shop.Billing.API/Setup/ApplicationConfiguration.cs", "AddDependencyOverrides(busNeedsDatabase: true);")
            .WithService("Acme.Shop.Stock", 5002, "HangfireSettings", "RabbitMq");

        var manifest = Read(files);

        manifest.FindService("Billing")!["Messaging"].ShouldBe("outbox");
        manifest.FindService("Stock")!["Messaging"].ShouldBe("direct");
        manifest.FindService("Orders")!["Messaging"].ShouldBe("none");
        // The root was made by the first command, without a bus, and the services generated later left it so.
        manifest.SolutionArgs["Messaging"].ShouldBe("none");
    }

    [Test]
    public void AServiceWithoutHangfireSettings_HasNoJobs()
    {
        var files = new Files().WithService(Orders, 5000);

        Read(files).FindService("Orders")!.IsOn("Hangfire").ShouldBeFalse();
    }

    [Test]
    public void AGateway_IsReadAsOne_WithItsPort()
    {
        var files = Solution().WithService("Acme.Shop.Gate", 5005)
            .With("src/services/Acme.Shop.Gate/Acme.Shop.Gate.API/Setup/GatewayTransforms.cs");

        var gate = Read(files).FindService("Gate")!;

        gate.IsOn("ApiGateway").ShouldBeTrue();
        gate.Port.ShouldBe(5005);
    }

    [Test]
    public void TheSolutionsChoices_AreReadFromWhatOnlyThatChoiceGenerates()
    {
        var files = Solution()
            .With("src/common/Acme.Shop.Common.Infrastructure/Persistence/SqlServer/SqlServerErrors.cs")
            .With("deploy/k8s/namespace.yaml")
            .With(".opencode/rules.md")
            .With("src/services/Acme.Shop.Orders/Acme.Shop.Orders.Tests/Acme.Shop.Orders.Tests.csproj", "<PackageReference Include=\"xunit.v3\" />")
            .With("src/common/Acme.Shop.Common.Infrastructure/Configuration/Secrets/VaultOptions.cs")
            .With(".github/workflows/ci.yml");

        var solution = Read(files).SolutionArgs;

        solution["Database"].ShouldBe("mssql");
        solution["Deploy"].ShouldBe("k8s");
        solution["Agent"].ShouldBe("opencode");
        solution["TestFramework"].ShouldBe("xunit");
        solution.IsOn("Vault").ShouldBeTrue();
        solution.IsOn("Infisical").ShouldBeFalse();
        solution.IsOn("GitHubCiCd").ShouldBeTrue();
    }

    [Test]
    public void AFolderOfTheTeam_WithoutAnApiProjectOfItsName_IsNotAService()
    {
        var files = Solution().With("src/services/Acme.Shop.Tools/Acme.Shop.Tools.Cli/Acme.Shop.Tools.Cli.csproj");

        Read(files).Services.Count.ShouldBe(1);
    }

    [Test]
    public void TheVersions_AreReadFromWhereTheTemplateWritesThem()
    {
        var files = Solution()
            .With("Directory.Build.props", "<DotNetSolutionKitVersion>2.7.0</DotNetSolutionKitVersion>")
            .With($"src/services/{Orders}/{Orders}.API/{Orders}.API.csproj", "<DotNetSolutionKitServiceVersion>2.6.0</DotNetSolutionKitServiceVersion>");

        SolutionReader.Version(files).ShouldBe("2.7.0");
        SolutionReader.ServiceVersion(files, Prefix, Orders).ShouldBe("2.6.0");
        SolutionReader.Version(new Files()).ShouldBeNull();
    }
}
