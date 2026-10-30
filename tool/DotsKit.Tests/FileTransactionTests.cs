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

    [Test]
    public async Task APathThroughALinkedFolder_IsRefused_AndNothingIsWritten()
    {
        var root = Folders.NewTemp("txn-link");
        var outside = Folders.NewTemp("txn-outside");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "deploy"), outside);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("creating a symbolic link needs a privilege this machine does not give");
        }

        var transaction = new FileTransaction(root);
        transaction.Write("deploy/evil.txt", [1]);
        transaction.Write("src/ok.txt", [2]);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => transaction.CommitAsync(() => Task.CompletedTask));

        refused.Message.ShouldContain("deploy/evil.txt leads outside the solution through a link");
        File.Exists(Path.Combine(outside, "evil.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(root, "src", "ok.txt")).ShouldBeFalse("the transaction writes all or nothing");
    }

    [Test]
    public void ACommittedSymbolicLink_IsCarriedAsALink_AndNeverRead()
    {
        var root = Folders.NewTemp("text-link");
        var secret = Path.Combine(Folders.NewTemp("text-outside"), "credentials.txt");
        File.WriteAllText(secret, "the secret");
        try
        {
            File.CreateSymbolicLink(Path.Combine(root, "linked.txt"), secret);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("creating a symbolic link needs a privilege this machine does not give");
        }

        var text = Merging.FileText.Read(root, "linked.txt")!;

        text.IsBinary.ShouldBeTrue();
        Encoding.UTF8.GetString(text.Raw!).ShouldNotContain("the secret");
    }

    [Test]
    public void AFileBehindALinkedFolder_IsNoFileOfTheSolution_AndNoRepairReachesIt()
    {
        var root = Folders.NewTemp("walk-link");
        var outside = Folders.NewTemp("walk-outside");
        Directory.CreateDirectory(Path.Combine(root, "src", "Api"));
        File.WriteAllText(Path.Combine(root, "src", "Api", "mine.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(outside, "theirs.csproj"), "<Project />");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "src", "vendor"), outside);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("creating a symbolic link needs a privilege this machine does not give");
        }

        var files = Folders.RelativeFiles(root).ToList();

        files.ShouldBe(["src/Api/mine.csproj"]);
        Folders.IsInside(root, Path.Combine(root, "src", "vendor", "theirs.csproj")).ShouldBeFalse();
        Folders.IsInside(root, Path.Combine(root, "src", "Api", "mine.csproj")).ShouldBeTrue();
    }

    [Test]
    public void TheToolsOwnFolders_AreUnderTheUsersLocalData_NotTheSharedTemp()
    {
        var folder = Folders.OwnFolder("outputs");

        folder.ShouldStartWith(Folders.RealPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        if (!OperatingSystem.IsWindows())
        {
            (File.GetUnixFileMode(folder) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)).ShouldBe(UnixFileMode.None);
        }
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
    public async Task Should_PutBackTheOthers_AndNameTheFile_When_OneCannotBePutBack()
    {
        var root = Solution();

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => Changes(root).CommitAsync(() =>
        {
            File.Delete(Path.Combine(root, "changed.txt"));
            Directory.CreateDirectory(Path.Combine(root, "changed.txt", "in the way"));
            throw new IOException("dotnet sln failed");
        }));

        failure.Message.ShouldStartWith("dotnet sln failed");
        failure.Message.ShouldContain("changed.txt");
        File.ReadAllText(Path.Combine(root, "changed.txt.dotskit-old")).ShouldBe("before");
        Read(root, "deleted.txt").ShouldBe("before");
        Read(root, "All.sln").ShouldBe("before");
        Directory.Exists(Path.Combine(root, "new")).ShouldBeFalse();
    }

    [Test]
    public async Task Should_SayANewFileIsToDelete_When_ItCannotBeRemoved()
    {
        var root = Solution();
        var added = Path.Combine(root, "new", "added.txt");

        InvalidOperationException failure;
        using (var refused = new Refused(Path.GetDirectoryName(added)!, added))
        {
            failure = await Should.ThrowAsync<InvalidOperationException>(() => Changes(root).CommitAsync(() =>
            {
                refused.Refuse();
                throw new IOException("dotnet sln failed");
            }));
        }

        failure.Message.ShouldContain("These new files could not be removed: ");
        failure.Message.ShouldContain("added.txt; delete them.");
        failure.Message.ShouldNotContain("could not be put back");
        Read(root, "changed.txt").ShouldBe("before");
    }

    [Test]
    public async Task Should_KeepTheWrite_When_TheCleanupIsRefused()
    {
        var root = Solution();
        var kept = Path.Combine(root, "changed.txt.dotskit-old");

        using (var refused = new Refused(root, kept))
        {
            await Changes(root).CommitAsync(() =>
            {
                refused.Refuse();
                return Task.CompletedTask;
            });
        }

        Read(root, "changed.txt").ShouldBe("after");
        File.Exists(Path.Combine(root, "deleted.txt")).ShouldBeFalse();
        File.ReadAllText(kept).ShouldBe("before");
    }

    /// <summary>
    /// A file the disk refuses to delete, as one a scanner holds: read-only on Windows, in a read-only folder elsewhere,
    /// where a read-only file is deleted all the same.
    /// </summary>
    private sealed class Refused(string folder, string file) : IDisposable
    {
        public void Refuse()
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(file, FileAttributes.ReadOnly);
            }
            else
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
        }

        public void Dispose()
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            else
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
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
        Directory.Exists(Path.Combine(root, "new")).ShouldBeFalse();
    }
}
