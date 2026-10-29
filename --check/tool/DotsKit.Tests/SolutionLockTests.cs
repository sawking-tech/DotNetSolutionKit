using DotsKit.Io;
using DotsKit.Tests.Scenarios;

namespace DotsKit.Tests;

/// <summary>One command at a time in a solution: a second one is refused, and the lock is let go with its holder.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class SolutionLockTests
{
    [Test]
    public void ASecondTake_OfOneSolution_IsRefused_UntilTheFirstLetsGo_AndAnotherSolutionIsFree()
    {
        var root = Folders.NewTemp("lock-solution");
        var other = Folders.NewTemp("lock-other");

        using (var first = SolutionLock.TryTake(root))
        {
            first.ShouldNotBeNull();
            SolutionLock.TryTake(root).ShouldBeNull();
            using var free = SolutionLock.TryTake(other);
            free.ShouldNotBeNull();
        }
        using var again = SolutionLock.TryTake(root);

        again.ShouldNotBeNull();
    }

    [Test]
    [Category(Sandbox.Category)]
    public async Task ADotskitStartedWhileAnotherHoldsTheSolution_SaysSoInOneLine_AndChangesNothing()
    {
        var solution = await Sandbox.SolutionAsync();
        using var held = SolutionLock.TryTake(solution.Root);

        var second = await solution.DotskitAsync("new", "-S", "Billing", "--yes", "--no-build");

        second.Code.ShouldBe((int)ExitCode.Failed);
        second.Error.ShouldContain("Another dotskit is running in this solution");
        solution.Exists("src/services/Acme.Shop.Billing").ShouldBeFalse();
    }
}
