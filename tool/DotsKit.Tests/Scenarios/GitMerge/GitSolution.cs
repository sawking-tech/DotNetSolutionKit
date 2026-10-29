using DotsKit.Io;

namespace DotsKit.Tests.Scenarios.GitMerge;

/// <summary>
/// What the scenarios of the merge by git share: a solution in git, as the template 1.0 gave it with the team's
/// line and a local secret git ignores, the template's outputs as folders, and the real git to run.
/// The scenarios of this folder and what each file checks: README.md beside this file.
/// </summary>
internal static class GitSolution
{
    public const string ProgramFile = "src/services/Acme.Shop.Orders/Program.cs";
    public const string Secrets = "src/services/Acme.Shop.Orders/appsettings.Secrets.json";
    public const string Solution = "src/services/Acme.Shop.All.sln";

    /// <summary>Program.cs as the template 1.0 gave it.</summary>
    public const string BaseProgram = "a\nb\nc\nd\ne\n";

    /// <summary>Program.cs of the solution: the template's, and the team's line at the end.</summary>
    public const string TeamsProgram = "a\nb\nc\nd\ne\nteam\n";

    /// <summary>Program.cs as the next version of the template gives it: a line of its own at the top.</summary>
    public const string NextProgram = "template\na\nb\nc\nd\ne\n";

    public static readonly ProcessRunner Runner = new();

    public static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>An output of the template: a folder with the files given.</summary>
    public static string Tree(params (string Path, string Text)[] files)
    {
        var root = Folders.NewTemp("git-merge-tree");
        foreach (var (path, text) in files)
        {
            Write(root, path, text);
        }

        return root;
    }

    public static void Write(string root, string path, string text)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public static void WriteWithBom(string root, string path, string text)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, [.. Bom, .. System.Text.Encoding.UTF8.GetBytes(text)]);
    }

    /// <summary>The text as LF: git writes a merged file by the machine's core.autocrlf.</summary>
    public static string Read(string root, string path) => File.ReadAllText(Path.Combine(root, path)).Replace("\r\n", "\n", StringComparison.Ordinal);

    public static async Task Git(string root, params string[] args) => await Runner.RunCheckedAsync("git", args, root, CancellationToken.None);

    public static async Task<string> HeadAsync(string root) =>
        (await Runner.RunCheckedAsync("git", ["rev-parse", "HEAD"], root, CancellationToken.None)).Trim();

    /// <summary>A solution as the template 1.0 gave it, committed, with a local secret git ignores and the team's line.</summary>
    public static async Task<string> SolutionAsync()
    {
        var root = Folders.NewTemp("git-merge-solution");
        Write(root, ".gitignore", "appsettings.Secrets.json\n");
        Write(root, ProgramFile, TeamsProgram);
        Write(root, Solution, "the team's own All.sln\n");
        Write(root, Secrets, "the team's secret\n");
        await Git(root, "init", "-q");
        await Git(root, "add", "-A");
        await Git(root, "commit", "-q", "-m", "the solution");
        return root;
    }

    /// <summary>The output of the template 1.0 the solution came from.</summary>
    public static string Base() =>
        Tree((".gitignore", "appsettings.Secrets.json\n"), (ProgramFile, BaseProgram), (Solution, "the template's All.sln\n"), (Secrets, "the template's secret\n"));
}
