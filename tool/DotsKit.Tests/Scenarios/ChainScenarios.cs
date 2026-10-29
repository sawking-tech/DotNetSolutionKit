using DotsKit.Generation;
using DotsKit.Io;

namespace DotsKit.Tests.Scenarios;

/// <summary>
/// A solution of the first release, 1.0.0, with code of the team that uses the types of Common, upgraded by
/// dotskit a release at a time to the last minor of each 2.x and then to the template's sources; every step
/// builds (#117). The releases before 2.6.1 come from their tags on GitHub, so the chain needs the network.
/// </summary>
[TestFixture]
[Category(Category)]
internal sealed class ChainScenarios
{
    /// <summary>Its own category: an hour of upgrades, run once in CI and not on every OS (template.yml).</summary>
    public const string Category = "Chain";

    private const string First = "1.0.0";

    /// <summary>The last patch of every minor of 2.x, in order.</summary>
    private static readonly string[] Releases = ["2.0.0", "2.1.3", "2.2.0", "2.3.0", "2.4.0", "2.5.0", "2.6.2", "2.7.0", "2.8.0"];

    private const string TeamFile = "src/services/Acme.Shop.Orders/Acme.Shop.Orders.Application/Team/OrderAudit.cs";

    private const string TeamCode = """
        using Acme.Shop.Common.Contracts.Domain.Context;
        using Acme.Shop.Common.Domain.Persistence;

        namespace Acme.Shop.Orders.Application.Team;

        // The team's own code: it uses types of Common that later versions of the template may move.
        public sealed class OrderAudit(IUserContext user, IUnitOfWork unitOfWork)
        {
            public IUserContext User { get; } = user;
            public IUnitOfWork UnitOfWork { get; } = unitOfWork;
        }
        """;

    /// <summary>The solution as 1.0.0 made it: Orders with Common and All.sln (-M false), then Billing, added by its script.</summary>
    private static async Task<Sandbox> SolutionOfTheFirstReleaseAsync()
    {
        var solution = await Sandbox.EmptyAsync();
        using var http = new HttpClient();
        var template = await new TagArchive(http).FolderOfAsync(First, CancellationToken.None);
        var hive = Folders.NewTemp("hive-" + First);
        (await solution.DotnetAsync("new", "install", template, "--debug:custom-hive", hive)).Code.ShouldBe(0);
        foreach (var args in new[] { new[] { "-S", "Orders", "-M", "false" }, ["-S", "Billing"] })
            await solution.DotnetAsync(["new", "DotNetSolutionKit", "-N", "Acme", "-P", "Shop", .. args, "--debug:custom-hive", hive]);
        await new Solutions.SolutionProjects(new ProcessRunner()).AddMissingAsync(solution.Root, CancellationToken.None);
        solution.Write(TeamFile, TeamCode);
        await solution.CommitAsync($"made by the template {First}, with the team's code");
        var init = await solution.DotskitAsync("init", "--template-version", First, "--yes");
        init.Code.ShouldBe(0, init.Output + init.Error);
        await solution.CommitAsync("described");
        return solution;
    }

    [Test]
    public async Task ASolutionOfTheFirstRelease_IsUpgradedAReleaseAtATime_AndBuildsAfterEveryStep()
    {
        var solution = await SolutionOfTheFirstReleaseAsync();

        foreach (var release in Releases)
        {
            var step = await solution.DotskitAsync("upgrade", "--to", release, "--yes");
            step.Code.ShouldBe(0, $"{First} -> {release}:\n{step.Output}{step.Error}");
            await solution.CommitAsync($"upgraded to {release}");
        }
        var last = await solution.DotskitAsync("upgrade", "--yes");

        last.Code.ShouldBe(0, last.Output + last.Error);
        solution.Read(TeamFile).ShouldContain("public sealed class OrderAudit");
    }
}
