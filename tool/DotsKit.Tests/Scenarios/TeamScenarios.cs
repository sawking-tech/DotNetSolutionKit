using DotsKit.Io;

namespace DotsKit.Tests.Scenarios;

/// <summary>
/// A solution several people work on: one adds a project by hand, one leaves a service out of All.sln, one
/// follows the rules, one types the name of a service there is already. Each row of that table is a test here,
/// run with the template alone and with dotskit, on every OS the CI has.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class TeamScenarios
{
    /// <summary>A conflict as both merges mark it: the tool labels its sides solution and template, git HEAD and dotskit/template.</summary>
    private const string ConflictMarker = "<<<<<<< ";

    private const string Orders = $"{Sandbox.Prefix}.Orders";
    private const string HandProject = $"{Sandbox.Prefix}.Tools/{Sandbox.Prefix}.Tools.Cli/{Sandbox.Prefix}.Tools.Cli.csproj";
    private const string HandFolder = "MyTools";

    /// <summary>The template's exit code when its files would overwrite files already there.</summary>
    private const int WouldOverwrite = 73;

    private static async Task AddHandProjectAsync(Sandbox solution)
    {
        await solution.DotnetAsync("new", "classlib", "-n", $"{Sandbox.Prefix}.Tools.Cli", "-o", $"src/services/{Sandbox.Prefix}.Tools/{Sandbox.Prefix}.Tools.Cli");
        await solution.DotnetAsync("sln", solution.SolutionFile, "add", $"src/services/{HandProject}", "--solution-folder", HandFolder);
        await solution.CommitAsync("a project by hand");
    }

    [Test]
    public async Task HandProjectInTheSolution_StaysInItsFolder_When_TheTemplateAddsAService()
    {
        var solution = await Sandbox.SolutionAsync();
        await AddHandProjectAsync(solution);

        (await solution.DotnetNewAsync("-N", "Acme", "-P", "Shop", "-S", "Stock", "--allow-scripts", "yes")).Code.ShouldBe(0);

        (await solution.ProjectsAsync()).ShouldContain(HandProject);
        solution.SolutionFolders().ShouldContain(HandFolder);
        solution.SolutionFolders().ShouldContain("Stock");
    }

    [Test]
    public async Task HandProjectInTheSolution_StaysInItsFolder_When_DotskitAddsAService()
    {
        var solution = await Sandbox.SolutionAsync();
        await AddHandProjectAsync(solution);

        (await solution.DotskitAsync("new", "-S", "Stock", "--yes", "--no-build")).Code.ShouldBe(0);

        (await solution.ProjectsAsync()).ShouldContain(HandProject);
        solution.SolutionFolders().ShouldContain(HandFolder);
        solution.SolutionFolders().ShouldContain("Stock");
    }

    [Test]
    public async Task ServiceLeftOutOfTheSolution_IsAddedAndNamed_When_TheTemplateAddsTheNextService()
    {
        var solution = await Sandbox.SolutionAsync();
        await solution.DotnetNewAsync("-N", "Acme", "-P", "Shop", "-S", "Sales.Billing", "--allow-scripts", "no");
        (await solution.ProjectsAsync()).ShouldNotContain(p => p.Contains("Billing"));

        var next = await solution.DotnetNewAsync("-N", "Acme", "-P", "Shop", "-S", "Stock", "--allow-scripts", "yes");

        (await solution.ProjectsAsync()).Count(p => p.Contains("Sales.Billing")).ShouldBe(5);
        solution.SolutionFolders().ShouldContain("Billing");
        next.Output.ShouldContain("Billing");
    }

    [Test]
    public async Task SameServiceAgain_WritesNothing_When_TheTemplateAloneIsUsed()
    {
        var solution = await Sandbox.SolutionAsync();
        var program = $"src/services/{Orders}/{Orders}.API/Program.cs";
        var before = solution.Read(program);

        var again = await solution.DotnetNewAsync("-N", "Acme", "-P", "Shop", "-S", "Orders", "--MongoDB", "true", "--allow-scripts", "yes");

        again.Code.ShouldBe(WouldOverwrite);
        solution.Read(program).ShouldBe(before);
    }

    [Test]
    public async Task SameServiceAgain_JoinsTheFlags_KeepsTheTeamsChanges_AndBuilds_When_DotskitIsUsed()
    {
        var solution = await Sandbox.SolutionAsync();
        var repository = $"src/services/{Orders}/{Orders}.Infrastructure/DependencyInjection.cs";
        solution.Write(repository, solution.Read(repository) + "\n// the team's line\n");
        await solution.CommitAsync("the team's change");

        var added = await solution.DotskitAsync("new", "-S", "orders", "--MongoDB", "true", "--yes");

        added.Code.ShouldBe((int)ExitCode.Done, added.Output + added.Error);
        solution.Read(repository).ShouldContain("// the team's line");
        solution.Read(Layout.ManifestPath).ShouldContain("MongoDB");
        solution.Read(Layout.ManifestPath).ShouldNotContain("\"orders\"", Case.Sensitive);
    }

    [Test]
    public async Task ALocalSecretTheTemplateChanges_StaysAsTheTeamWroteIt_InTheMergeByFiles()
    {
        var solution = await Sandbox.SolutionAsync();
        var secrets = $"src/services/{Orders}/{Orders}.API/appsettings.Secrets.json";
        var mine = solution.Read(secrets).Replace("\"DefaultConnection\": \"\"", "\"DefaultConnection\": \"Host=mine\"", StringComparison.Ordinal);
        solution.Write(secrets, mine);

        var changed = await solution.DotskitAsync("new", "-S", "Orders", "--Hangfire", "false", "--merge", "files", "--yes", "--no-build");

        changed.Code.ShouldBe((int)ExitCode.Done, changed.Output + changed.Error);
        solution.Read(secrets).ShouldBe(mine);
    }

    [Test]
    public async Task ServiceNamedAgainInAnotherCase_IsNotASecondService_When_TheTemplateAloneIsUsed()
    {
        var solution = await Sandbox.SolutionAsync();

        var again = await solution.DotnetNewAsync("-N", "Acme", "-P", "Shop", "-S", "orders", "--allow-scripts", "yes");

        if (OperatingSystem.IsLinux())
        {
            (again.Output + again.Error).ShouldContain("already", Case.Insensitive);
        }
        else
        {
            again.Code.ShouldBe(WouldOverwrite);
        }
    }

    [TestCase(Merging.MergeMode.Git)]
    [TestCase(Merging.MergeMode.Files)]
    public async Task FileTheTeamAddedWhereTheTemplateAddsOne_IsAConflict_NotOverwritten(string merge)
    {
        var solution = await Sandbox.SolutionAsync();
        const string mongo = "deploy/compose/infra/mongo.yml";
        solution.Write(mongo, "# the team's own mongo\n");
        await solution.CommitAsync("the team's mongo");

        var added = await solution.DotskitAsync("new", "-S", "Orders", "--MongoDB", "true", "--yes", "--no-build", "--merge", merge);

        added.Code.ShouldBe((int)ExitCode.Conflicts, added.Output + added.Error);
        solution.Read(mongo).ShouldContain("# the team's own mongo");
        solution.Read(mongo).ShouldContain(ConflictMarker);
    }

    [TestCase(Merging.MergeMode.Git)]
    [TestCase(Merging.MergeMode.Files)]
    public async Task LineTheTeamChangedWhereTheTemplateChangesIt_IsAMarkedConflict_WithExitCodeTwo(string merge)
    {
        var solution = await Sandbox.SolutionAsync();
        var configuration = $"src/services/{Orders}/{Orders}.API/Setup/ApplicationConfiguration.cs";
        const string template = "busNeedsDatabase: false);";
        const string teams = "busNeedsDatabase: false); // the team's note";
        solution.Write(configuration, solution.Read(configuration).Replace(template, teams));
        await solution.CommitAsync("the team's note on the bus line");

        // The outbox turns this very line into busNeedsDatabase: true.
        var added = await solution.DotskitAsync("new", "-S", "Orders", "--Messaging", "outbox", "--yes", "--no-build", "--merge", merge);

        added.Code.ShouldBe((int)ExitCode.Conflicts, added.Output + added.Error);
        added.Output.ShouldContain(configuration);
        solution.Read(configuration).ShouldContain(ConflictMarker);
        solution.Read(configuration).ShouldContain(teams);
        solution.Read(configuration).ShouldContain("busNeedsDatabase: true);");
    }

    [TestCase(Merging.MergeMode.Git)]
    [TestCase(Merging.MergeMode.Files)]
    public async Task LineTheTeamChangedRightBesideWhatTheTemplateAdds_IsAMarkedConflict_NotAMerge(string merge)
    {
        var solution = await Sandbox.SolutionAsync();
        const string env = "deploy/compose/.env.example";
        const string teams = "HANGFIRE_DASHBOARD_PASSWORD=the-teams-own-password";
        solution.Write(env, solution.Read(env).Replace("HANGFIRE_DASHBOARD_PASSWORD=", teams));
        await solution.CommitAsync("the team's password line");

        // The template's email block starts on the line right after the team's, with Hangfire on and ClickHouse
        // off as the sandbox has them: git merge-file takes changes that touch for one place.
        var added = await solution.DotskitAsync("new", "-S", "Notifications", "--Notify", "email", "--yes", "--no-build", "--merge", merge);

        added.Code.ShouldBe((int)ExitCode.Conflicts, added.Output + added.Error);
        added.Output.ShouldContain(env);
        solution.Read(env).ShouldContain(ConflictMarker);
        solution.Read(env).ShouldContain(teams);
        solution.Read(env).ShouldContain("SMTP_HOST=mailhog");
    }

    [Test]
    public async Task AGatewayAsTheFirstService_GivesASolutionThatBuilds()
    {
        var folder = Path.Combine(Folders.NewTemp("scenario-gateway"), "solution");
        Directory.CreateDirectory(folder);

        var made = await Sandbox.DotskitIn(folder, ["new", "-N", "Acme", "-P", "Shop", "-S", "Gate", "--ApiGateway", "true", "--yes"]);

        made.Code.ShouldBe((int)ExitCode.Done, made.Output + made.Error);
    }

    [Test]
    public async Task NoAnswer_WritesNothing_When_DotskitRunsWithoutYes()
    {
        var solution = await Sandbox.SolutionAsync();

        var refused = await solution.DotskitAsync("new", "-S", "Stock", "--no-build");

        refused.Code.ShouldBe((int)ExitCode.Failed);
        solution.Exists($"src/services/{Sandbox.Prefix}.Stock").ShouldBeFalse();
    }

    [Test]
    public async Task Dotskit_RefusesAnUncommittedTree()
    {
        var solution = await Sandbox.SolutionAsync();
        solution.Write("notes.txt", "not committed");

        var refused = await solution.DotskitAsync("new", "-S", "Stock", "--yes", "--no-build");

        refused.Code.ShouldBe((int)ExitCode.Failed);
        refused.Error.ShouldContain("uncommitted");
    }
}
