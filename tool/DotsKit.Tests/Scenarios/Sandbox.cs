using DotsKit.Io;

namespace DotsKit.Tests.Scenarios;

/// <summary>
/// A generated solution to act on as a team would: the template from this repository's sources, the tool as
/// the command line a person runs, git around it. Each test gets its own copy of one solution made once.
/// </summary>
internal sealed class Sandbox
{
    public const string Category = "Scenario";
    public const string Prefix = "Acme.Shop";

    private const string SourcesVariable = "DOTSKIT_TEMPLATE_SOURCE";

    private static readonly ProcessRunner Runner = new();
    private static readonly Lazy<Task<string>> Reference = new(MakeReferenceAsync);
    private static readonly Lazy<Task<string>> Hive = new(InstallTemplateAsync);

    public string Root { get; }

    private Sandbox(string root) => Root = root;

    /// <summary>A solution with the service Orders, made by dotskit and committed.</summary>
    public static async Task<Sandbox> SolutionAsync()
    {
        var root = Path.Combine(Folders.NewTemp("scenario"), "solution");
        Folders.Copy(await Reference.Value, root);
        var sandbox = new Sandbox(root);
        await sandbox.GitAsync("init", "-q");
        await sandbox.CommitAsync("generated");
        return sandbox;
    }

    /// <summary>An empty folder under git, for a solution the test makes itself.</summary>
    public static async Task<Sandbox> EmptyAsync()
    {
        var root = Path.Combine(Folders.NewTemp("scenario"), "solution");
        Directory.CreateDirectory(root);
        var sandbox = new Sandbox(root);
        await sandbox.GitAsync("init", "-q");
        return sandbox;
    }

    /// <summary>The folder with template/.template.config, found from where the tests run.</summary>
    public static string TemplateSources
    {
        get
        {
            for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
                if (Directory.Exists(Path.Combine(folder.FullName, "template", ".template.config")))
                    return Path.Combine(folder.FullName, "template");
            throw new InvalidOperationException("The template's sources are not above the tests' folder.");
        }
    }

    public Task<ProcessResult> DotskitAsync(params string[] args) => DotskitIn(Root, args);

    /// <summary>The template alone, as without the tool: dotnet new in the solution's root.</summary>
    public async Task<ProcessResult> DotnetNewAsync(params string[] args) =>
        await Runner.RunAsync("dotnet", ["new", "DotNetSolutionKit", .. args, "--debug:custom-hive", await Hive.Value], Root, CancellationToken.None);

    public async Task<ProcessResult> DotnetAsync(params string[] args) => await Runner.RunAsync("dotnet", args, Root, CancellationToken.None);

    public async Task CommitAsync(string message)
    {
        await GitAsync("add", "-A");
        await GitAsync("-c", "user.name=test", "-c", "user.email=test@example.com", "commit", "-q", "--allow-empty", "-m", message);
    }

    public string SolutionFile => Path.Combine(Root, "src", "services", $"{Prefix}.All.sln");

    /// <summary>The projects of All.sln, relative to it, with forward slashes.</summary>
    public async Task<HashSet<string>> ProjectsAsync() =>
        await new Solutions.SolutionProjects(Runner).ListAsync(SolutionFile, CancellationToken.None);

    /// <summary>The solution folders of All.sln by name.</summary>
    public IReadOnlyList<string> SolutionFolders() =>
        [.. File.ReadAllLines(SolutionFile)
            .Where(l => l.StartsWith("Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\")", StringComparison.Ordinal))
            .Select(l => l.Split('"')[3])];

    public string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    public void Write(string path, string text)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public bool Exists(string path) => File.Exists(Path.Combine(Root, path)) || Directory.Exists(Path.Combine(Root, path));

    private async Task GitAsync(params string[] args) => await Runner.RunCheckedAsync("git", args, Root, CancellationToken.None);

    /// <summary>The tool these scenarios run, as dotnet runs it.</summary>
    private static string Tool => Path.Combine(AppContext.BaseDirectory, "dotskit.dll");

    /// <summary>How git runs the tool again for its merge driver, as the tool says it of itself.</summary>
    public static string Self => SelfCommand.Of("dotnet", Tool);

    public static Task<ProcessResult> DotskitIn(string folder, IReadOnlyList<string> args)
    {
        Environment.SetEnvironmentVariable(SourcesVariable, TemplateSources);
        return Runner.RunAsync("dotnet", [Tool, .. args], folder, CancellationToken.None);
    }

    private static async Task<string> MakeReferenceAsync()
    {
        var root = Path.Combine(Folders.NewTemp("reference"), "solution");
        Directory.CreateDirectory(root);
        var made = await DotskitIn(root, ["new", "-N", "Acme", "-P", "Shop", "-S", "Orders", "--yes", "--no-build"]);
        if (made.Code != 0)
            throw new InvalidOperationException($"The reference solution was not made:\n{made.Output}{made.Error}");
        return root;
    }

    private static async Task<string> InstallTemplateAsync()
    {
        var hive = Folders.NewTemp("scenario-hive");
        await Runner.RunCheckedAsync("dotnet", ["new", "install", TemplateSources, "--force", "--debug:custom-hive", hive], hive, CancellationToken.None);
        return hive;
    }
}
