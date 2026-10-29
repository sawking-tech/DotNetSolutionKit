namespace DotsKit.Tests.Scenarios;

/// <summary>
/// A solution made with the template alone, as before dotskit: init describes it, the template generates it
/// again by that description and reproduces every file, and dotskit then adds to it.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class InitScenarios
{
    private const string Reproduced = " files of the template reproduced.";

    /// <summary>The solution with its first service, then each service generated into it, by the template alone.</summary>
    private static async Task<Sandbox> MadeByTheTemplateAsync(string[] solution, params string[][] services)
    {
        var made = await Sandbox.EmptyAsync();
        await GenerateAsync(made, [.. solution, "--Solution"]);
        foreach (var service in services)
        {
            await GenerateAsync(made, [.. solution.Where((_, i) => i < 4), .. service]);
        }

        await made.CommitAsync("made by the template alone");
        return made;
    }

    private static async Task GenerateAsync(Sandbox solution, string[] args)
    {
        var result = await solution.DotnetNewAsync([.. args, "--allow-scripts", "yes"]);
        result.Code.ShouldBe(0, result.Output + result.Error);
    }

    [Test]
    public async Task ASolutionWithFlagsOnItsServices_IsReproducedWhole_AndDotskitAddsToIt()
    {
        var solution = await MadeByTheTemplateAsync(
            ["-N", "Acme", "-P", "Shop", "-S", "Orders", "--Storage", "true", "--MongoDB", "true", "--Notify", "email", "--Messaging", "outbox", "--Audit", "true", "--DiffApi", "true", "--FeatureFlags", "true", "--ClickHouse", "true"],
            ["-S", "Billing", "--Storage", "true", "--Messaging", "outbox", "--Notify", "email", "--Audit", "true"],
            ["-S", "Stock", "--Hangfire", "false", "--Messaging", "direct", "--ClickHouse", "true", "--FeatureFlags", "true"]);

        var init = await solution.DotskitAsync("init", "--yes");

        init.Code.ShouldBe(0, init.Output + init.Error);
        init.Output.ShouldContain(Reproduced);
        var manifest = Manifest.Parse(solution.Read(Layout.ManifestPath));
        manifest.FindService("Stock")!.IsOn("Hangfire").ShouldBeFalse();
        manifest.FindService("Billing")!["Messaging"].ShouldBe("outbox");
        await solution.CommitAsync("described");

        var added = await solution.DotskitAsync("new", "-S", "Catalog", "--MongoDB", "true", "--yes");
        added.Code.ShouldBe(0, added.Output + added.Error);
    }

    [Test]
    public async Task ASolutionOfOtherChoices_AndAGateway_IsReproducedWhole()
    {
        var solution = await MadeByTheTemplateAsync(
            ["-N", "Acme", "-P", "Shop", "-S", "Orders", "--Database", "mssql", "--Deploy", "k8s", "--TestFramework", "xunit", "--Agent", "opencode", "--GitHubCiCd", "true", "--Vault", "true", "--Infisical", "true", "--HierarchyRules", "true"],
            ["-S", "Gate", "--ApiGateway", "true", "--Database", "mssql", "--Deploy", "k8s", "--TestFramework", "xunit", "--Agent", "opencode"],
            ["-S", "Billing", "--Database", "mssql", "--Deploy", "k8s", "--TestFramework", "xunit", "--Agent", "opencode", "--Vault", "true"]);

        var init = await solution.DotskitAsync("init", "--yes");

        init.Code.ShouldBe(0, init.Output + init.Error);
        init.Output.ShouldContain(Reproduced);
        Manifest.Parse(solution.Read(Layout.ManifestPath)).FindService("Gate")!.IsOn("ApiGateway").ShouldBeTrue();
    }

    [Test]
    public async Task ASolutionOf27FromNuGet_IsReproducedWhole_ByThatVersion()
    {
        var solution = await Sandbox.EmptyAsync();
        var hive = Io.Folders.NewTemp("hive-27");
        (await solution.DotnetAsync("new", "install", "SawKing.DotNetSolutionKit::2.7.0", "--debug:custom-hive", hive)).Code.ShouldBe(0);
        foreach (var args in new[] { new[] { "-S", "Orders", "-M", "false" }, ["-S", "Billing"] })
        {
            var made = await solution.DotnetAsync(["new", "DotNetSolutionKit", "-N", "Acme", "-P", "Shop", .. args, "--Storage", "true", "--Messaging", "outbox", "--debug:custom-hive", hive]);
            made.Code.ShouldBe(0, made.Output + made.Error);
        }
        // 2.7 left a service out of All.sln until manual-add-projects.sh, which adds it as this does.
        await new Solutions.SolutionProjects(new Io.ProcessRunner()).AddMissingAsync(solution.Root, CancellationToken.None);
        await solution.CommitAsync("made by the template 2.7.0");

        var init = await solution.DotskitAsync("init", "--yes");

        init.Code.ShouldBe(0, init.Output + init.Error);
        init.Output.ShouldContain(Reproduced);
        Manifest.Parse(solution.Read(Layout.ManifestPath)).Template.ShouldBe("2.7.0");
    }

    [Test]
    public async Task AServiceTheTemplateAddedToASolutionOfDotskit_IsAddedToItsManifest()
    {
        var solution = await Sandbox.SolutionAsync();
        await GenerateAsync(solution, ["-N", "Acme", "-P", "Shop", "-S", "Stock", "--HttpPort", "5100"]);
        await solution.CommitAsync("a service by the template alone");

        var init = await solution.DotskitAsync("init", "--yes");

        init.Code.ShouldBe(0, init.Output + init.Error);
        init.Output.ShouldContain(Reproduced);
        Manifest.Parse(solution.Read(Layout.ManifestPath)).FindService("Stock")!.Port.ShouldBe(5100);
    }
}
