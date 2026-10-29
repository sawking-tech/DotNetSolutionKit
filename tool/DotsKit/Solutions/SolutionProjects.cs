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
    /// The projects a generated tree has, as All.sln lists them, relative to it: the template's All.sln lists every
    /// project under src, so the files say it without a <c>dotnet sln list</c>.
    /// </summary>
    public static HashSet<string> InTree(string root) =>
        Directory.GetFiles(Path.Combine(root, "src"), ProjectPattern, SearchOption.AllDirectories)
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
        var listed = await runner.RunCheckedAsync("dotnet", ["sln", solutionFile, "list"], DirectoryOf(solutionFile), ct);
        return listed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.EndsWith(Layout.ProjectExtension, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>One <c>dotnet sln add</c> per solution folder, each a process of half a second or more.</summary>
    public async Task AddAsync(string solutionFile, IReadOnlyCollection<string> listedProjects, CancellationToken ct)
    {
        foreach (var folder in listedProjects.GroupBy(FolderOfListed, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            await runner.RunCheckedAsync("dotnet",
                ["sln", solutionFile, "add", .. folder.Order(StringComparer.Ordinal), "--solution-folder", folder.Key], DirectoryOf(solutionFile), ct);
    }

    /// <summary>One <c>dotnet sln remove</c> for them all; none when there is nothing to remove.</summary>
    public async Task RemoveAsync(string solutionFile, IReadOnlyCollection<string> listedProjects, CancellationToken ct)
    {
        if (listedProjects.Count > 0)
            await runner.RunCheckedAsync("dotnet", ["sln", solutionFile, "remove", .. listedProjects.Order(StringComparer.Ordinal)], DirectoryOf(solutionFile), ct);
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
