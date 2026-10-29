using DotsKit.Io;

namespace DotsKit.Tests;

/// <summary>A list of paths cut into batches, each a command line that fits.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class CommandLineTests
{
    [Test]
    public void ThePaths_AreCutIntoBatchesThatFit_InTheirOrder_AndNoneIsLost()
    {
        var paths = Enumerable.Range(0, 100).Select(n => $"project{n:D2}.csproj").ToList();

        var batches = CommandLine.Batches(paths, ["sln", "All.sln", "remove"], maxLength: 200).ToList();

        batches.Count.ShouldBeGreaterThan(1);
        batches.SelectMany(b => b).ShouldBe(paths);
        batches.ShouldAllBe(b => "sln All.sln remove".Length + b.Sum(p => p.Length + 3) <= 200);
    }

    [Test]
    public void AFewPaths_AreOneBatch_AndNoneAreNoBatch()
    {
        CommandLine.Batches(["a.csproj", "b.csproj"], ["sln", "All.sln", "add"]).Single().ShouldBe(["a.csproj", "b.csproj"]);
        CommandLine.Batches([], ["sln", "All.sln", "remove"]).ShouldBeEmpty();
    }

    [Test]
    public void APathTooLongForABatchOfItsOwn_IsRefused_BeforeTheFirstBatchOfItsList()
    {
        var tooLong = new string('a', 200) + ".csproj";

        var paths = Enumerable.Range(0, 100).Select(n => $"project{n:D2}.csproj").Append(tooLong);
        using var batches = CommandLine.Batches(paths, ["sln", "All.sln", "add"], maxLength: 200).GetEnumerator();

        var refused = Should.Throw<InvalidOperationException>(() => batches.MoveNext());

        refused.Message.ShouldContain(tooLong);
        refused.Message.ShouldContain("a shorter path");
    }
}
