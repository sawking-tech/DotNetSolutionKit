using System.Text;
using DotsKit.Io;

namespace DotsKit.Tests;

[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class FileTransactionTests
{
    private static byte[] B(string text) => Encoding.UTF8.GetBytes(text);

    private static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path));

    private static string Solution()
    {
        var root = Folders.NewTemp("tx");
        File.WriteAllText(Path.Combine(root, "kept.txt"), "kept");
        File.WriteAllText(Path.Combine(root, "changed.txt"), "before");
        File.WriteAllText(Path.Combine(root, "deleted.txt"), "before");
        File.WriteAllText(Path.Combine(root, "All.sln"), "before");
        return root;
    }

    private static FileTransaction Changes(string root)
    {
        var transaction = new FileTransaction(root);
        transaction.Write("changed.txt", B("after"));
        transaction.Write("new/added.txt", B("added"));
        transaction.Delete("deleted.txt");
        transaction.Guard("All.sln");
        return transaction;
    }

    [Test]
    public async Task Should_WriteEverything_When_NothingFails()
    {
        var root = Solution();

        await Changes(root).CommitAsync(() => File.WriteAllTextAsync(Path.Combine(root, "All.sln"), "after"));

        Read(root, "changed.txt").ShouldBe("after");
        Read(root, "new/added.txt").ShouldBe("added");
        File.Exists(Path.Combine(root, "deleted.txt")).ShouldBeFalse();
        Read(root, "All.sln").ShouldBe("after");
        Directory.GetFiles(root, "*.dotskit-*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    public async Task Should_PutEveryFileBack_When_AStepInsideFails()
    {
        var root = Solution();

        await Should.ThrowAsync<InvalidOperationException>(() => Changes(root).CommitAsync(async () =>
        {
            await File.WriteAllTextAsync(Path.Combine(root, "All.sln"), "half written");
            throw new InvalidOperationException("dotnet sln failed");
        }));

        Read(root, "changed.txt").ShouldBe("before");
        Directory.Exists(Path.Combine(root, "new")).ShouldBeFalse();
        Read(root, "deleted.txt").ShouldBe("before");
        Read(root, "All.sln").ShouldBe("before");
        Read(root, "kept.txt").ShouldBe("kept");
        Directory.GetFiles(root, "*.dotskit-*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    public async Task Should_RemoveTheFolder_When_ItsLastFileIsDeleted()
    {
        var root = Solution();
        Directory.CreateDirectory(Path.Combine(root, "dropped/project"));
        File.WriteAllText(Path.Combine(root, "dropped/project/a.csproj"), "<Project />");
        var transaction = new FileTransaction(root);
        transaction.Delete("dropped/project/a.csproj");

        await transaction.CommitAsync(() => Task.CompletedTask);

        Directory.Exists(Path.Combine(root, "dropped")).ShouldBeFalse();
        Directory.Exists(root).ShouldBeTrue();
    }

    [Test]
    public async Task Should_WriteNothing_When_AFileCannotBeStaged()
    {
        var root = Solution();
        Directory.CreateDirectory(Path.Combine(root, "blocked.txt.dotskit-new"));
        var transaction = Changes(root);
        transaction.Write("blocked.txt", B("x"));

        await Should.ThrowAsync<Exception>(() => transaction.CommitAsync(() => Task.CompletedTask));

        Read(root, "changed.txt").ShouldBe("before");
        Read(root, "deleted.txt").ShouldBe("before");
        File.Exists(Path.Combine(root, "new/added.txt")).ShouldBeFalse();
    }
}
