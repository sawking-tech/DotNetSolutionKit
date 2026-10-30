using DotsKit.Io;
using DotsKit.Merging;

namespace DotsKit.Tests;

/// <summary>The parts of the merge by git that decide: what the template changes between two outputs, and the option.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class GitModeTests
{
    private const string File = "src/a.cs";

    private static FileText Text(string text) => new(text, Bom: false);

    [Test]
    public void TheTemplatesChange_IsAFileAddedRemovedOrChanged_AndAFileItKeptIsNone()
    {
        TemplateDiff.Decide(File, null, Text("a\n"))!.Kind.ShouldBe(ChangeKind.Add);
        TemplateDiff.Decide(File, Text("a\n"), null)!.Kind.ShouldBe(ChangeKind.Delete);
        TemplateDiff.Decide(File, Text("a\n"), Text("b\n"))!.Kind.ShouldBe(ChangeKind.Update);
        TemplateDiff.Decide(File, Text("a\n"), Text("a\r\n")).ShouldBeNull();
    }

    [Test]
    public void ASolutionThereAlready_IsMergedByGit_UnlessMergeFilesIsNamed()
    {
        Options.Parse("upgrade", ["--yes"]).ByGit.ShouldBeTrue();
        Options.Parse("upgrade", ["--merge", "files"]).ByGit.ShouldBeFalse();
        Options.Parse("new", ["-S", "Billing", "--merge", "git"]).ByGit.ShouldBeTrue();
        Should.Throw<ArgumentException>(() => Options.Parse("upgrade", ["--merge", "svn"])).Message.ShouldContain("name git or files");
        Should.Throw<ArgumentException>(() => Options.Parse("init", ["--merge", "files"])).Message.ShouldContain("init merges nothing");
    }

    [Test]
    public async Task TheFilesGitHidesTheChangesOf_AreNamed_AndNoneOutsideGit()
    {
        var runner = new ProcessRunner();
        var root = Folders.NewTemp("hidden");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "src", "a.json"), "{}");
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "src", "b.json"), "{}");
        var outside = await new Git(runner).HiddenChangesAsync(root, CancellationToken.None);
        await runner.RunCheckedAsync("git", ["init", "-q"], root, CancellationToken.None);
        await runner.RunCheckedAsync("git", ["add", "-A"], root, CancellationToken.None);
        await runner.RunCheckedAsync("git", ["update-index", "--assume-unchanged", "src/a.json"], root, CancellationToken.None);
        // skip-worktree hides local changes the same way, and a merge or a reset writes over them too.
        await runner.RunCheckedAsync("git", ["update-index", "--skip-worktree", "src/b.json"], root, CancellationToken.None);

        var hidden = await new Git(runner).HiddenChangesAsync(root, CancellationToken.None);

        outside.ShouldBeEmpty();
        hidden.ShouldBe(["src/a.json", "src/b.json"], ignoreOrder: true);
    }

    [Test]
    public void AFileNotInUtf8_IsCarriedAsItsBytes_AndARepairLeavesIt()
    {
        var root = Folders.NewTemp("cp1251");
        var file = Path.Combine(root, "Old.cs");
        // "using Old;" and a comment in Windows-1251: bytes no UTF-8 decoder takes.
        byte[] bytes = [.. "using Old;\n// "u8, 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2, (byte)'\n'];
        System.IO.File.WriteAllBytes(file, bytes);

        var applied = new Solutions.RepairWriter().ApplyAsync([new Solutions.ReplaceUsing(file, "Old", "New")], CancellationToken.None).Result;

        FileText.Read(root, "Old.cs")!.IsBinary.ShouldBeTrue();
        applied.ShouldBeEmpty();
        System.IO.File.ReadAllBytes(file).ShouldBe(bytes);
    }

    [Test]
    public async Task TheFilesTheSolutionsGitIgnores_AreNamed_AndNoneOutsideGit()
    {
        var runner = new ProcessRunner();
        var root = Folders.NewTemp("ignored");
        Directory.CreateDirectory(Path.Combine(root, "src", "Api"));
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "**/appsettings.Secrets.json\n");
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "src", "Api", "appsettings.Secrets.json"), "{}\n");
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "src", "Api", "appsettings.json"), "{}\n");
        var outsideGit = await new GitIgnoredFiles(runner).InAsync(root, CancellationToken.None);
        await runner.RunCheckedAsync("git", ["init", "-q"], root, CancellationToken.None);

        var ignored = await new GitIgnoredFiles(runner).InAsync(root, CancellationToken.None);

        ignored.ShouldBe(["src/Api/appsettings.Secrets.json"]);
        outsideGit.ShouldBeEmpty();
    }

    [Test]
    public async Task TheFolderOfASolution_InItsRepository_IsNamed_AndTheRootIsNone()
    {
        var runner = new ProcessRunner();
        var root = Folders.NewTemp("prefix");
        Directory.CreateDirectory(Path.Combine(root, "backend"));
        await runner.RunCheckedAsync("git", ["init", "-q"], root, CancellationToken.None);

        (await new Git(runner).PrefixAsync(Path.Combine(root, "backend"), CancellationToken.None)).ShouldBe("backend/");
        (await new Git(runner).PrefixAsync(root, CancellationToken.None)).ShouldBeEmpty();
    }

    /// <summary>A merge by git that merges nothing and names the files of the next output it left off the branch.</summary>
    private sealed class LeavingOut(params string[] leftOut) : IGitMerge
    {
        public IReadOnlySet<string> LeftOut { get; } = leftOut.ToHashSet();

        public Task<IReadOnlyList<string>> MergeAsync(string root, TemplateCommit @base, TemplateCommit next, bool preferTemplate, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task StageAsync(string root, IReadOnlyList<string> paths, CancellationToken ct) => Task.CompletedTask;

        public Task AbortAsync(string root, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoBuild : Solutions.IBuilder
    {
        public Task<ProcessResult> BuildAsync(string root, CancellationToken ct) => Task.FromResult(new ProcessResult(0, "", ""));
    }

    [Test]
    public async Task AFileTheBranchLeftOut_IsWrittenBesideTheMerge_AndANewFileGitPutElsewhereIsNot()
    {
        const string secrets = "src/services/Stock/Stock.API/appsettings.Secrets.json";
        const string moved = "src/services/Stock/Stock.API/New.cs";
        var root = Folders.NewTemp("left-out");
        Directory.CreateDirectory(Path.Combine(root, "src", "services"));
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "src", "services", "Acme.Shop.All.sln"), "");
        var manifest = new Manifest { Template = "2.9.0", Solution = ["--NamespaceRoot", "Acme", "--ProductName", "Shop"], Services = [] };
        Change[] added = [new(secrets, ChangeKind.Add, Text("{}\n"), "new in the template"), new(moved, ChangeKind.Add, Text("class New;\n"), "new in the template")];
        var plan = new Planning.Plan(root, manifest, added, new Solutions.MembershipChange([], []), ByGit: (new TemplateCommit("base", "2.8.0"), new TemplateCommit("next", "2.9.0")));
        var writer = new Planning.PlanWriter(new Solutions.SolutionProjects(new ProcessRunner()), new LeavingOut(secrets),
            new Solutions.ReferenceRepair(new Solutions.RepairWriter(), new NoBuild()));

        await writer.WriteAsync(plan, CancellationToken.None);

        System.IO.File.Exists(Path.Combine(root, secrets)).ShouldBeTrue();
        System.IO.File.Exists(Path.Combine(root, moved)).ShouldBeFalse();
    }
}
