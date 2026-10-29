using DotsKit.Commands;
using DotsKit.Generation;
using DotsKit.Io;
using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>
/// Every state of a folder dotskit init meets, row by row of its table: a solution on disk, the generation that
/// checks the reading faked by a copy of the solution itself, so no template runs.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class InitCommandTests
{
    private const string ToolVersion = "2.8.0";
    private const string Orders = "Acme.Shop.Orders";

    private sealed class Fakes
    {
        public bool Answer { get; init; } = true;
        public bool OnSources { get; init; }
        public int Staged { get; private set; }
        public List<string> Lines { get; } = [];

        /// <summary>The releases the tags name.</summary>
        public IReadOnlyList<string> Releases { get; init; } = [];

        /// <summary>The release whose output is the solution as it is; every other one gives a file more. Null: all are.</summary>
        public string? Closest { get; init; }

        /// <summary>A release the template fails to generate by, as dotnet new fails on a choice it does not know.</summary>
        public string? Failing { get; init; }

        public InitCommand Command() => new(new Staging(this), new Projects(this), new TagReleases(this), new Terminal(this), ToolVersion, OnSources);

        /// <summary>What the template "gives" is the solution as it is, so the check reproduces every file.</summary>
        private sealed class Staging(Fakes fakes) : IStaging
        {
            public Task<string> BuildAsync(Manifest manifest, bool withSolutionFile, CancellationToken ct)
            {
                lock (fakes)
                {
                    fakes.Staged++;
                }

                if (manifest.Template == fakes.Failing)
                {
                    throw new InvalidOperationException("dotnet new DotNetSolutionKit --output C:/t/staged exited with 127:\nan unknown choice\n");
                }

                var staged = Folders.NewTemp("init-staged");
                Folders.Copy(fakes.Root!, staged);
                if (fakes.Closest is { } closest && manifest.Template != closest)
                {
                    File.WriteAllText(Path.Combine(staged, "src", "common", "Added.cs"), "// not in the solution\n");
                }

                return Task.FromResult(staged);
            }
        }

        private sealed class TagReleases(Fakes fakes) : ITemplateReleases
        {
            public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct) => Task.FromResult(fakes.Releases);
        }

        /// <summary>The solution's All.sln lists what its files are, as a generated one does.</summary>
        private sealed class Projects(Fakes fakes) : ISolutionProjects
        {
            public Task<HashSet<string>> ListAsync(string solutionFile, CancellationToken ct) => Task.FromResult(SolutionProjects.InTree(fakes.Root!));
            public Task AddMissingAsync(string root, CancellationToken ct) => Task.CompletedTask;
            public Task RemoveMissingAsync(string root, CancellationToken ct) => Task.CompletedTask;
            public Task AddAsync(string solutionFile, IReadOnlyCollection<string> listedProjects, CancellationToken ct) => Task.CompletedTask;
            public Task RemoveAsync(string solutionFile, IReadOnlyCollection<string> listedProjects, CancellationToken ct) => Task.CompletedTask;
        }

        private sealed class Terminal(Fakes fakes) : ITerminal
        {
            public void Info(string line) => fakes.Lines.Add(line);

            public void Error(string line) => fakes.Lines.Add(line);

            public bool Confirm(string question) => fakes.Answer;
        }

        public string? Root { get; set; }

        public Task<ExitCode> RunAsync(string root, params string[] args)
        {
            Root = root;
            return Command().RunAsync(Options.Parse(args), root, CancellationToken.None);
        }
    }

    /// <summary>A solution of the template on disk: All.sln, Common, the version, and the service Orders.</summary>
    private static string Solution(string? version = "2.8.0", bool common = true)
    {
        var root = Folders.NewTemp("init-command");
        Write(root, "src/services/Acme.Shop.All.sln", "");
        Service(root, Orders, 5000);
        if (common)
        {
            Write(root, "src/common/Acme.Shop.Common/Acme.Shop.Common.csproj", "<Project />");
        }

        if (version is not null)
        {
            Write(root, "Directory.Build.props", $"<Project><DotNetSolutionKitVersion>{version}</DotNetSolutionKitVersion></Project>");
        }

        return root;
    }

    private static void Service(string root, string folder, int port, string? version = null)
    {
        var api = $"src/services/{folder}/{folder}.API";
        Write(root, $"{api}/{folder}.API.csproj", version is null ? "<Project />" : $"<Project><DotNetSolutionKitServiceVersion>{version}</DotNetSolutionKitServiceVersion></Project>");
        Write(root, $"{api}/Properties/launchSettings.json", $"{{ \"applicationUrl\": \"http://localhost:{port}\" }}");
        Write(root, $"{api}/appsettings.json", "{ \"HangfireSettings\": {} }");
    }

    private static void Write(string root, string path, string text)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static string ManifestOf(string root) => File.ReadAllText(Path.Combine(root, Layout.ManifestPath));

    private static bool HasManifest(string root) => File.Exists(Path.Combine(root, Layout.ManifestPath));

    [Test]
    public async Task AnEmptyFolder_WritesNothing_AndNamesDotskitNew()
    {
        var folder = Folders.NewTemp("init-empty");

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(folder));

        refused.Message.ShouldContain("dotskit new -N <Company> -P <Product> -S <Service>");
    }

    [Test]
    public async Task AFolderWithoutAllSln_IsNotASolution()
    {
        var folder = Folders.NewTemp("init-other");
        Write(folder, "README.md", "not a solution");

        (await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(folder))).Message.ShouldContain("Not a solution of DotNetSolutionKit");
    }

    [Test]
    public async Task ASolutionOf28_IsReadCheckedAndWritten_AfterAYes()
    {
        var root = Solution();
        var fakes = new Fakes();

        (await fakes.RunAsync(root)).ShouldBe(ExitCode.Done);

        fakes.Staged.ShouldBe(1);
        fakes.Lines.ShouldContain(l => l.Contains("files of the template reproduced."));
        Manifest.Parse(ManifestOf(root)).FindService("Orders")!.Port.ShouldBe(5000);
    }

    [Test]
    public async Task NoAnswer_WritesNothing()
    {
        var root = Solution();

        (await new Fakes { Answer = false }.RunAsync(root)).ShouldBe(ExitCode.Failed);

        HasManifest(root).ShouldBeFalse();
    }

    [Test]
    public async Task ASolutionThatDoesNotSayItsVersion_IsCheckedByEachMinorBefore27_AndTheClosestIsNamed_NothingWritten()
    {
        var root = Solution(version: null);
        var fakes = new Fakes { Releases = ["1.0.0", "2.0.0", "2.1.0", "2.1.3", "2.6.2", "2.7.0", "2.8.0"], Closest = "2.1.3" };

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => fakes.RunAsync(root));

        refused.Message.ShouldContain("dotskit init --template-version 2.1.3");
        fakes.Staged.ShouldBe(4);
        fakes.Lines.ShouldContain(l => l.StartsWith("  2.1.3: ", StringComparison.Ordinal) && l.EndsWith("(100%)", StringComparison.Ordinal));
        fakes.Lines.ShouldNotContain(l => l.StartsWith("  2.7.0", StringComparison.Ordinal));
        HasManifest(root).ShouldBeFalse();
    }

    [Test]
    public async Task AReleaseThatCannotBeGenerated_IsItsLineInTheTable_AndTheOthersStillDecide()
    {
        var root = Solution(version: null);
        var fakes = new Fakes { Releases = ["1.0.0", "2.0.0", "2.1.3"], Closest = "2.1.3", Failing = "2.0.0" };

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => fakes.RunAsync(root));

        refused.Message.ShouldContain("dotskit init --template-version 2.1.3");
        fakes.Lines.ShouldContain("  2.0.0: not generated - an unknown choice");
        HasManifest(root).ShouldBeFalse();
    }

    [Test]
    public async Task NoReleaseThatCanBeGenerated_AsksForTheVersion()
    {
        var root = Solution(version: null);
        var fakes = new Fakes { Releases = ["2.0.0"], Failing = "2.0.0" };

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => fakes.RunAsync(root));

        refused.Message.ShouldContain("no release could generate the solution again");
        refused.Message.ShouldContain("--template-version 2.x.y");
        HasManifest(root).ShouldBeFalse();
    }

    [Test]
    public void OfTwoReleasesThatReproduceAsMuch_TheNewerIsTheClosest()
    {
        var same = new Reading.Reproduction(10, ["a.cs"]);

        VersionAdvice.Closest([new("2.1.3", same), new("2.6.2", same), new("2.0.0", null, "failed")]).ShouldBe("2.6.2");
        VersionAdvice.Closest([new("2.0.0", null, "failed")]).ShouldBeNull();
    }

    [Test]
    public async Task ASolutionThatDoesNotSayItsVersion_AsksForIt_WhenNoReleaseIsListed()
    {
        var root = Solution(version: null);

        (await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(root))).Message.ShouldContain("--template-version 2.x.y");
        HasManifest(root).ShouldBeFalse();
    }

    [Test]
    public async Task TheVersionTheCommandNames_IsTheManifests()
    {
        var root = Solution(version: null);

        (await new Fakes().RunAsync(root, "--template-version", "2.6.2")).ShouldBe(ExitCode.Done);

        Manifest.Parse(ManifestOf(root)).Template.ShouldBe("2.6.2");
    }

    [Test]
    public async Task ASolutionOfTheTemplatesSources_AsksForTheRelease_UnlessTheToolRunsOnThem()
    {
        (await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(Solution("source")))).Message.ShouldContain("--template-version");
        (await new Fakes { OnSources = true }.RunAsync(Solution("source"))).ShouldBe(ExitCode.Done);
    }

    [Test]
    public async Task ASolutionNewerThanTheTool_NamesTheUpdate()
    {
        var refused = await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(Solution("2.9.0")));

        refused.Message.ShouldContain("dotnet tool update -g SawKing.DotsKit.Tool");
    }

    [Test]
    public async Task ASolutionOfAnEarlierMajor_IsNotDescribed()
    {
        (await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(Solution("1.4.0")))).Message.ShouldContain("upgrading.md");
    }

    [Test]
    public async Task AManifestThatListsEveryService_IsCurrent_AndNothingIsGenerated()
    {
        var root = Solution();
        await new Fakes().RunAsync(root);
        var fakes = new Fakes();

        (await fakes.RunAsync(root)).ShouldBe(ExitCode.Done);

        fakes.Staged.ShouldBe(0);
        fakes.Lines.ShouldContain("The manifest is current: 2.8.0, services Orders.");
    }

    [Test]
    public async Task AServiceTheManifestDoesNotList_IsAdded()
    {
        var root = Solution();
        await new Fakes().RunAsync(root);
        Service(root, "Acme.Shop.Billing", 5001);

        (await new Fakes().RunAsync(root)).ShouldBe(ExitCode.Done);

        var manifest = Manifest.Parse(ManifestOf(root));
        manifest.ServiceArgs.Select(s => s.Service).ShouldBe(["Orders", "Billing"]);
        manifest.FindService("Billing")!.Port.ShouldBe(5001);
    }

    [Test]
    public async Task ServicesWithoutCommon_GetTheirManifest_AndNoCheck()
    {
        var root = Solution(common: false);
        var fakes = new Fakes();

        (await fakes.RunAsync(root)).ShouldBe(ExitCode.Done);

        fakes.Staged.ShouldBe(0);
        fakes.Lines.ShouldContain(l => l.StartsWith("No Common: the solution's database and flags", StringComparison.Ordinal));
        HasManifest(root).ShouldBeTrue();
    }

    [Test]
    public async Task AServiceOfAnotherVersion_IsNamed_AndNothingWritten()
    {
        var root = Solution("2.8.0");
        Service(root, "Acme.Shop.Billing", 5001, version: "2.7.0");

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(root));

        refused.Message.ShouldContain("Billing is 2.7.0");
        refused.Message.ShouldContain("--template-version 2.8.0");
        HasManifest(root).ShouldBeFalse();
    }

    [Test]
    public async Task AServiceOfAnotherVersion_IsDescribedByTheVersionTheCommandNames()
    {
        var root = Solution("2.8.0");
        Service(root, "Acme.Shop.Billing", 5001, version: "2.8.1");

        (await new Fakes().RunAsync(root, "--template-version", "2.8.0")).ShouldBe(ExitCode.Done);

        Manifest.Parse(ManifestOf(root)).Template.ShouldBe("2.8.0");
    }

    [Test]
    public async Task AnUnlistedServiceOfAnotherVersion_IsNamed_AsWhenTheSolutionIsDescribed()
    {
        var root = Solution();
        await new Fakes().RunAsync(root);
        Service(root, "Acme.Shop.Billing", 5001, version: "2.8.1");

        (await Should.ThrowAsync<InvalidOperationException>(() => new Fakes().RunAsync(root))).Message.ShouldContain("Billing is 2.8.1");
    }

    [Test]
    public async Task AVersionOtherThanTheManifests_IsRefused_WhenTheManifestGetsAService()
    {
        var root = Solution();
        await new Fakes().RunAsync(root);
        Service(root, "Acme.Shop.Billing", 5001);

        (await Should.ThrowAsync<ArgumentException>(() => new Fakes().RunAsync(root, "--template-version", "2.7.0"))).Message.ShouldContain("The manifest is 2.8.0");
    }

    [Test]
    public async Task AParameterOtherThanTheNames_IsRefused_SinceInitReadsThem()
    {
        var refused = await Should.ThrowAsync<ArgumentException>(() => new Fakes().RunAsync(Solution(), "--Storage", "true"));

        refused.Message.ShouldContain("--Storage true");
    }

    [Test]
    public async Task AnEmptyFolderInsideASolution_FindsTheSolution()
    {
        var root = Solution();
        var inside = Path.Combine(root, "docs");
        Directory.CreateDirectory(inside);
        var fakes = new Fakes { Root = root };

        (await fakes.Command().RunAsync(Options.Parse([]), inside, CancellationToken.None)).ShouldBe(ExitCode.Done);

        HasManifest(root).ShouldBeTrue();
    }

    [Test]
    public void TheTemplateVersionOption_BelongsToInit()
    {
        Should.Throw<ArgumentException>(() => Options.Parse("new", ["-S", "Billing", "--template-version", "2.7.0"]));
        Options.Parse("init", ["--template-version", "2.7.0"]).TemplateVersion.ShouldBe("2.7.0");
    }
}
