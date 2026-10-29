using DotsKit.Generation;
using DotsKit.Io;
using DotsKit.Planning;
using DotsKit.Solutions;

namespace DotsKit.Commands;

/// <summary>
/// <c>dotskit new -S &lt;Service&gt; [flags]</c>: no solution around - makes one with this service; a solution -
/// adds the service, or joins the flags to a service it has. Whether to make or to add is read from the
/// folder; <c>--Solution</c> changes nothing.
/// </summary>
/// <param name="onSources">The tool runs against the template's sources: it generates with them, as version "source".</param>
internal sealed class NewCommand(IPlanner planner, PlanExecution execution, IGit git, string toolVersion, bool onSources)
{
    private string Target => onSources ? TemplateSource.SourcesVersion : toolVersion;

    public async Task<ExitCode> RunAsync(Options options, string folder, CancellationToken ct)
    {
        var command = TemplateArgs.Parse(options.TemplateArgs);
        if (!command.HasService)
            throw new ArgumentException("Name the service with -S.");
        var root = SolutionRoot.Find(folder);
        if (root is null)
            await RefuseFilesOutsideGitAsync(folder, options, ct);
        var current = root is null ? null : await LoadCheckedAsync(root, options, ct);
        var next = Next(current, command);
        var plan = await planner.BuildAsync(root ?? folder, current, next, new Merging.MergeMode(options.ByGit, options.Force), ct);
        return await execution.RunAsync(plan, options, ct);
    }

    /// <summary>
    /// A new solution goes into an empty folder, or beside files a clean git tree holds: what the tool writes over
    /// them must be possible to see and undo.
    /// </summary>
    private async Task RefuseFilesOutsideGitAsync(string folder, Options options, CancellationToken ct)
    {
        if (!SolutionRoot.IsEmpty(folder) && !options.AllowDirty && !await git.IsCleanAsync(folder, ct))
            throw new InvalidOperationException("The folder has files outside a clean git tree: make the solution in an empty folder, or commit what is here first (--allow-dirty to go on).");
    }

    /// <summary>The manifest of a solution to add to: there, of this version, and the tree clean.</summary>
    private async Task<Manifest> LoadCheckedAsync(string root, Options options, CancellationToken ct)
    {
        var manifest = Manifest.Load(root)
            ?? throw new InvalidOperationException($"{Layout.ManifestPath} is missing: describe the solution first, dotskit init.");
        var version = Target;
        if (manifest.Template != version)
            throw new InvalidOperationException(TemplateVersions.IsNewer(manifest.Template, version)
                ? $"The solution is of the template {manifest.Template}, newer than dotskit {version}: update the tool, dotnet tool update -g SawKing.DotsKit.Tool."
                : $"The solution is of the template {manifest.Template}, dotskit is {version}: bring the solution to {version} first, dotskit upgrade.");
        if (!options.AllowDirty && !await git.IsCleanAsync(root, ct))
            throw new InvalidOperationException("The git working tree has uncommitted changes: commit or stash them, so what dotskit writes can be seen and undone (--allow-dirty to go on).");
        if (options.AllowDirty && options.ByGit)
            throw new ArgumentException("--allow-dirty needs --merge files: a git merge runs on a clean tree.");
        return manifest;
    }

    /// <summary>The manifest with the command's service: a new solution's first, or one more of the solution's.</summary>
    private Manifest Next(Manifest? current, TemplateArgs command)
    {
        var service = Ports.Assign(command.ServicePart(), current);
        if (current is null)
            return new Manifest { Template = Target, Solution = command.SolutionPart().ToArgs(), Services = [] }.WithService(service);
        RefuseOtherSolutionValues(current, command);
        return current.WithService(service);
    }

    /// <summary>A service takes the names, the database and the rest from the solution; a different one would break it.</summary>
    private static void RefuseOtherSolutionValues(Manifest current, TemplateArgs command)
    {
        var different = current.OtherSolutionValues(command);
        if (different.Count > 0)
            throw new ArgumentException($"The solution has its own {string.Join(", ", different.Select(n => $"{n} ({current.SolutionArgs[n]})"))}: a service takes them from it, leave them out of the command.");
    }
}
