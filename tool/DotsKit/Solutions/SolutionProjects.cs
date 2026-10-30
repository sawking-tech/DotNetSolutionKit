using DotsKit.Io;

namespace DotsKit.Solutions;

/// <summary>
/// The projects of All.sln, through <c>dotnet sln</c>, never by editing its text: <c>dotnet sln add</c> gives a
/// project a new GUID each time, so two All.sln with the same projects differ as text. A service's projects go
/// into a solution folder named after the last segment of the service folder's name
/// (NamespaceRoot.ProductName.Sales.Orders -> Orders), Common's into Common - as the template's post-action does.
/// </summary>
internal interface ISolutionProjects
{
    Task<HashSet<string>> ListAsync(string solutionFile, CancellationToken ct);
    Task AddMissingAsync(string root, CancellationToken ct);
    Task RemoveMissingAsync(string root, CancellationToken ct);
    Task AddAsync(string solutionFile, IReadOnlyCollection<string> listedProjects, CancellationToken ct);
    Task RemoveAsync(string solutionFile, IReadOnlyCollection<string> listedProjects, CancellationToken ct);
}

internal sealed class SolutionProjects(IProcessRunner runner) : ISolutionProjects
{
    /// <summary>A service's projects sit one level down in its folder: src/services/&lt;service&gt;/&lt;project&gt;/*.csproj.</summary>
    private const int ProjectDepth = 1;

    private const string ProjectPattern = "*.csproj";

    /// <summary>
    /// <c>dotnet sln add</c> loads the project it adds through MSBuild, which imports the Directory.*.props and
    /// .targets above it; a merge that left one of them in conflict, as Directory.Build.props, has markers there that
    /// MSBuild cannot read. These MSBuild switches, read from the environment, leave them out. They are the second try
    /// only: the team's props may set the platforms of its projects, and an add that reads them maps the new project
    /// to those platforms in All.sln.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> WithoutDirectoryImports = new Dictionary<string, string>
    {
        ["ImportDirectoryBuildProps"] = "false",
        ["ImportDirectoryBuildTargets"] = "false",
        ["ImportDirectoryPackagesProps"] = "false",
    };

    /// <summary>
    /// The projects a generated tree has, as All.sln lists them, relative to it: the template's All.sln lists every
    /// project under src, so the files say it without a <c>dotnet sln list</c>.
    /// </summary>
    public static HashSet<string> InTree(string root) =>
        Directory.GetFiles(Path.Combine(root, Layout.SourceFolder), ProjectPattern, SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(Layout.ServicesOf(root), p).Replace('\\', '/'))
            .Where(p => !p.Split('/').Any(Layout.Ignored.Contains))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string FolderOf(string serviceFolderName) => serviceFolderName[(serviceFolderName.LastIndexOf('.') + 1)..];

    /// <summary>The solution folder a project listed relative to All.sln belongs in.</summary>
    public static string FolderOfListed(string project)
    {
        var first = project.Split('/')[0];
        return first == ".." ? Layout.CommonSolutionFolder : FolderOf(first);
    }

    public async Task<HashSet<string>> ListAsync(string solutionFile, CancellationToken ct)
    {
        // Before anything reads or rewrites the file: dotnet sln rewrites All.sln on add and remove, and it drops
        // a Project line that misses its EndProject without a word - the solution loses a project of the team's.
        if (WhyMalformed(await File.ReadAllTextAsync(solutionFile, ct)) is { } why)
        {
            throw new InvalidOperationException($"{solutionFile} {why}: fix the pairing, then run again.");
        }

        var listed = await runner.RunCheckedAsync("dotnet", ["sln", solutionFile, "list"], DirectoryOf(solutionFile), ct);
        return listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.EndsWith(Layout.ProjectExtension, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Why the text of a solution file is not safe to hand to dotnet sln; null when it is. Each Project line is
    /// closed by its EndProject before the next: dotnet sln silently drops an unclosed one when it rewrites the file.
    /// </summary>
    public static string? WhyMalformed(string text)
    {
        var open = 0;
        var openedAt = 0;
        var line = 0;
        foreach (var raw in text.Split('\n'))
        {
            line++;
            var trimmed = raw.TrimEnd();
            if (trimmed.StartsWith("Project(", StringComparison.Ordinal))
            {
                if (open > 0)
                {
                    return $"is malformed: the Project of line {openedAt} has no EndProject before the Project of line {line}";
                }

                (open, openedAt) = (1, line);
            }
            else if (trimmed == "EndProject")
            {
                if (open == 0)
                {
                    return $"is malformed: the EndProject of line {line} closes no Project";
                }

                open = 0;
            }
        }

        return open > 0 ? $"is malformed: the Project of line {openedAt} has no EndProject" : null;
    }

    /// <summary>Adds every service project of the solution All.sln does not list yet.</summary>
    public async Task AddMissingAsync(string root, CancellationToken ct)
    {
        var solutionFile = Layout.SolutionFileOf(root);
        var listed = await ListAsync(solutionFile, ct);
        await AddAsync(solutionFile, [.. ServiceProjects(root).Select(p => Relative(solutionFile, p)).Where(p => !listed.Contains(p))], ct);
    }

    /// <summary>Removes from All.sln every project whose file is gone, as when a service folder is generated again.</summary>
    public async Task RemoveMissingAsync(string root, CancellationToken ct)
    {
        var solutionFile = Layout.SolutionFileOf(root);
        var gone = (await ListAsync(solutionFile, ct)).Where(p => !File.Exists(Path.Combine(DirectoryOf(solutionFile), p))).ToList();
        await RemoveAsync(solutionFile, gone, ct);
    }

    /// <summary>
    /// One <c>dotnet sln add</c> per solution folder, each a process of half a second or more; a folder with more
    /// projects than one command line holds takes a batch at a time (<see cref="CommandLine"/>).
    /// </summary>
    public async Task AddAsync(string solutionFile, IReadOnlyCollection<string> listedProjects, CancellationToken ct)
    {
        foreach (var folder in listedProjects.GroupBy(FolderOfListed, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            string[] command = ["sln", solutionFile, "add"], options = ["--solution-folder", folder.Key];
            foreach (var batch in CommandLine.Batches(folder.Order(StringComparer.Ordinal), [.. command, .. options]))
            {
                await AddBatchAsync(solutionFile, [.. command, .. batch, .. options], ct);
            }
        }
    }

    /// <summary>One <c>dotnet sln remove</c> for as many as one command line holds; none when there is nothing to remove.</summary>
    public async Task RemoveAsync(string solutionFile, IReadOnlyCollection<string> listedProjects, CancellationToken ct)
    {
        string[] command = ["sln", solutionFile, "remove"];
        foreach (var batch in CommandLine.Batches(listedProjects.Order(StringComparer.Ordinal), command))
        {
            await runner.RunCheckedAsync("dotnet", [.. command, .. batch], DirectoryOf(solutionFile), ct);
        }
    }

    /// <summary>One batch of <c>dotnet sln add</c>, again without the Directory imports when it fails; an add that fails writes nothing.</summary>
    private async Task AddBatchAsync(string solutionFile, string[] args, CancellationToken ct)
    {
        if ((await runner.RunAsync("dotnet", args, DirectoryOf(solutionFile), ct)).Code != 0)
        {
            await runner.RunCheckedAsync("dotnet", args, DirectoryOf(solutionFile), ct, WithoutDirectoryImports);
        }
    }

    private static IEnumerable<string> ServiceProjects(string root) =>
        Directory.GetDirectories(Layout.ServicesOf(root))
            .SelectMany(service => Directory.GetFiles(service, ProjectPattern,
                new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = ProjectDepth }))
            .Order(StringComparer.Ordinal);

    private static string DirectoryOf(string solutionFile) => Path.GetDirectoryName(solutionFile)!;

    private static string Relative(string solutionFile, string project) =>
        Path.GetRelativePath(DirectoryOf(solutionFile), project).Replace('\\', '/');
}
