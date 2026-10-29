using DotsKit.Io;

namespace DotsKit.Generation;

/// <summary>Generates with one version of the template.</summary>
internal interface ITemplate
{
    /// <summary>Runs the template in <paramref name="root"/>, its post-actions with it.</summary>
    Task GenerateAsync(TemplateArgs args, string root, CancellationToken ct);
}

/// <summary>Makes the template of a version; the version a manifest names may be older than the tool.</summary>
internal interface ITemplateSource
{
    ITemplate Of(string version);
}

/// <summary>
/// The template installed in a hive of the tool's own, so the templates the user installed stay as they are.
/// The package comes from nuget.org by version; a release before it was there, from its tag on GitHub
/// (<see cref="TagArchive"/>); a folder of the template's sources stands in for it when the tool runs against the
/// template being developed (<see cref="TemplateSource.SourcesFolder"/>).
/// </summary>
/// <param name="runsScripts">
/// The version has the post-action that adds a service to All.sln by a command, and takes --allow-scripts for it;
/// a version without one refuses the option (<see cref="TemplateVersions.AddsServiceToSolution"/>).
/// </param>
/// <param name="package">What <c>dotnet new install</c> takes: a package and its version, or a folder; found once, on the first use.</param>
internal sealed class DotnetNewTemplate(Func<CancellationToken, Task<string>> package, bool runsScripts, IProcessRunner runner) : ITemplate
{
    private const string ShortName = "DotNetSolutionKit";

    /// <summary>
    /// dotnet new's code for a post-action it does not run: a version before 2.8 names manual-add-projects.sh in
    /// one, and the tool adds the service to All.sln itself (<see cref="Staging"/>).
    /// </summary>
    private const int ManualPostAction = 105;

    private readonly string _hive = Folders.NewTemp("hive");
    /// <summary>The one install of this hive: the base and the next tree of one version may ask for it at once.</summary>
    private readonly object _installing = new();
    private Task? _install;

    public async Task GenerateAsync(TemplateArgs args, string root, CancellationToken ct)
    {
        await InstallOnceAsync(ct);
        Directory.CreateDirectory(root);
        List<string> command = ["new", ShortName, .. args.ToArgs(), "--output", root, .. ScriptsAllowed(), "--debug:custom-hive", _hive];
        var result = await runner.RunAsync("dotnet", command, root, ct);
        if (result.Code != 0 && !(result.Code == ManualPostAction && !runsScripts))
            throw new InvalidOperationException($"dotnet {string.Join(' ', command)} exited with {result.Code}:\n{(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim()}");
    }

    private string[] ScriptsAllowed() => runsScripts ? ["--allow-scripts", "yes"] : [];

    private Task InstallOnceAsync(CancellationToken ct)
    {
        lock (_installing)
            _install ??= InstallAsync();
        return _install.WaitAsync(ct);
    }

    private async Task InstallAsync() =>
        await runner.RunCheckedAsync("dotnet", ["new", "install", await package(CancellationToken.None), "--force", "--debug:custom-hive", _hive],
            Path.GetTempPath(), CancellationToken.None);
}

internal sealed class TemplateSource(IProcessRunner runner, ITagArchive tags) : ITemplateSource
{
    private const string PackageId = "SawKing.DotNetSolutionKit";
    private const string SourcesVariable = "DOTSKIT_TEMPLATE_SOURCE";

    /// <summary>The version a manifest records when the tool ran against the template's sources.</summary>
    public const string SourcesVersion = "source";

    /// <summary>The folder of the template's sources, when DOTSKIT_TEMPLATE_SOURCE names one.</summary>
    public static string? SourcesFolder => Environment.GetEnvironmentVariable(SourcesVariable) is { Length: > 0 } folder ? folder : null;

    private readonly Dictionary<string, ITemplate> _templates = new(StringComparer.Ordinal);

    public ITemplate Of(string version)
    {
        // The base and the next tree are generated side by side and may ask for one version at once.
        lock (_templates)
            return OfLocked(version);
    }

    private ITemplate OfLocked(string version)
    {
        if (!_templates.TryGetValue(version, out var template))
            _templates[version] = template = new DotnetNewTemplate(ct => PackageOfAsync(version, ct), TemplateVersions.AddsServiceToSolution(version), runner);
        return template;
    }

    private Task<string> PackageOfAsync(string version, CancellationToken ct)
    {
        if (version == SourcesVersion)
            return Task.FromResult(SourcesFolder ?? throw new InvalidOperationException($"The solution was generated from the template's sources: set {SourcesVariable} to their folder."));
        return TemplateVersions.IsPublished(version) ? Task.FromResult($"{PackageId}::{version}") : tags.FolderOfAsync(version, ct);
    }
}
