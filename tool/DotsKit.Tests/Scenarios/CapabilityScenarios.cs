namespace DotsKit.Tests.Scenarios;

/// <summary>
/// A solution grows by need: it starts without MongoDB, the bus and Hangfire, and a service arrives that needs
/// one. dotskit new brings the capability with the service that names it: the project, its versions, its compose
/// file, its test server and its place in All.sln.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
[TestFixture]
[Category(Sandbox.Category)]
[Parallelizable(ParallelScope.All)]
internal sealed class CapabilityScenarios
{
    private const string Mongo = "src/capabilities/ST.Demo.Capabilities.Persistence/ST.Demo.Capabilities.Persistence.Mongo/ST.Demo.Capabilities.Persistence.Mongo.csproj";
    private const string RabbitMq = "src/capabilities/ST.Demo.Capabilities.Messaging/ST.Demo.Capabilities.Messaging.RabbitMQ/ST.Demo.Capabilities.Messaging.RabbitMQ.csproj";
    private const string Hangfire = "src/capabilities/ST.Demo.Capabilities.Background/ST.Demo.Capabilities.Background.Hangfire/ST.Demo.Capabilities.Background.Hangfire.csproj";

    [Test]
    public async Task MongoAndTheBus_ArriveWithTheService_ThatNamesThem()
    {
        var solution = await ASolutionWithoutCapabilitiesAsync();

        var billing = await solution.DotskitAsync("new", "-S", "Billing", "--MongoDB", "true", "--Messaging", "outbox", "-H", "false", "--yes");

        billing.Code.ShouldBe((int)ExitCode.Done, billing.Output + billing.Error);
        billing.Output.ShouldContain("the solution builds");
        solution.Exists(Mongo).ShouldBeTrue();
        solution.Exists(RabbitMq).ShouldBeTrue();
        solution.Exists("src/package-versions/mongodb.props").ShouldBeTrue();
        solution.Exists("deploy/compose/infra/mongo.yml").ShouldBeTrue();
        solution.Exists("deploy/compose/infra/rabbitmq.yml").ShouldBeTrue();
        solution.Exists("tests/servers/mongo.sh").ShouldBeTrue();
        (await solution.ProjectsAsync()).ShouldContain("../capabilities/ST.Demo.Capabilities.Persistence/ST.Demo.Capabilities.Persistence.Mongo/ST.Demo.Capabilities.Persistence.Mongo.csproj");
        (await solution.ProjectsAsync()).ShouldContain("../capabilities/ST.Demo.Capabilities.Messaging/ST.Demo.Capabilities.Messaging.RabbitMQ/ST.Demo.Capabilities.Messaging.RabbitMQ.csproj");
    }

    [Test]
    public async Task Hangfire_ArrivesWithTheService_ThatNamesIt()
    {
        var solution = await ASolutionWithoutCapabilitiesAsync();

        var jobs = await solution.DotskitAsync("new", "-S", "Jobs", "--yes");

        jobs.Code.ShouldBe((int)ExitCode.Done, jobs.Output + jobs.Error);
        jobs.Output.ShouldContain("the solution builds");
        solution.Exists(Hangfire).ShouldBeTrue();
        (await solution.ProjectsAsync()).ShouldContain("../capabilities/ST.Demo.Capabilities.Background/ST.Demo.Capabilities.Background.Hangfire/ST.Demo.Capabilities.Background.Hangfire.csproj");
    }

    /// <summary>A solution made by the template alone without MongoDB, the bus and Hangfire, committed.</summary>
    private static async Task<Sandbox> ASolutionWithoutCapabilitiesAsync()
    {
        var solution = await Sandbox.EmptyAsync();
        var made = await solution.DotnetNewAsync("-N", "ST", "-P", "Demo", "-S", "Orders", "--Solution", "-H", "false");

        made.Code.ShouldBe(0, made.Output + made.Error);
        await solution.CommitAsync("generated");
        return solution;
    }
}
