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
/// <param name="planner">Builds the plan: the trees of the template, their merge with the solution and the report.</param>
/// <param name="execution">Shows the plan, asks, writes it and builds.</param>
/// <param name="git">Tells whether the solution's working tree is clean, and whether it is at the root of its repository, before anything is written.</param>
/// <param name="toolVersion">The version of the tool, the template's version a new service is generated with.</param>
/// <param name="onSources">The tool runs against the template's sources: it generates with them, as version "source".</param>
internal sealed class NewCommand(IPlanner planner, PlanExecution execution, IGit git, string toolVersion, bool onSources)
{
    private string Target => onSources ? TemplateSource.SourcesVersion : toolVersion;

    public async Task<ExitCode> RunAsync(Options options, string folder, CancellationToken ct)
    {
        var command = TemplateArgs.Parse(options.TemplateArgs);
        if (!command.HasService)
        {
            throw new ArgumentException("Name the service with -S.");
        }

        if (TemplateVersions.WhyNotATransport(command["Messaging"], Target) is { } whyNotATransport)
        {
            throw new ArgumentException(whyNotATransport);
        }

        var root = SolutionRoot.Find(folder);
        var empty = root is null && SolutionRoot.IsEmpty(folder);
        if (root is null && !empty)
        {
            await RequireAGitRepoAsync(folder, ct);
            await GitPreconditions.CheckAsync(git, folder, options, ct);
        }

        var current = root is null ? null : await LoadCheckedAsync(root, options, ct);
        if (root is null)
        {
            // An empty folder takes the plain write, with nothing to take in; beside files, git merges like an upgrade.
            return empty
                ? await RunAsync(folder, null, command, options, TreeStart.None, ct)
                : await TakingUncommittedAsync(folder, null, command, options, ct);
        }

        // Adding to a solution is a merge like an upgrade: --allow-dirty takes the uncommitted changes in, the same way.
        return await TakingUncommittedAsync(root, current, command, options, ct);
    }

    private async Task<ExitCode> RunAsync(string root, Manifest? current, TemplateArgs command, Options options, TreeStart start, CancellationToken ct)
    {
        var plan = await planner.BuildAsync(root, current, Next(current, command), new Merging.MergeMode(options.Force, options.MarkBase), ct);
        return await execution.RunAsync(plan, options, start, ct);
    }

    /// <summary>
    /// --allow-dirty: the uncommitted changes committed by plumbing beside the run, so the merge runs on them; a run
    /// that fails before the merge stands puts them back uncommitted with git reset --mixed, an open merge keeps them.
    /// </summary>
    private async Task<ExitCode> TakingUncommittedAsync(string root, Manifest? current, TemplateArgs command, Options options, CancellationToken ct)
    {
        var taken = options.AllowDirty
            ? await git.CommitUncommittedAsync(root, "dotskit: uncommitted changes, taken into the change", ct)
            : null;
        try
        {
            var start = await TreeStart.OfAsync(git, root, options, ct);
            return await RunAsync(root, current, command, options, start, ct);
        }
        catch (Exception failure)
        {
            if (taken is { } uncommitted && !await git.MergeInProgressAsync(root, ct))
            {
                try
                {
                    await git.UncommitAsync(root, uncommitted.Previous, ct);
                }
                catch (Exception uncommit)
                {
                    throw new InvalidOperationException(
                        $"{failure.Message} The uncommitted changes are left committed as {uncommitted.Commit[..7]}: run git reset --mixed {uncommitted.Previous} to put them back uncommitted.", uncommit);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// A new solution beside files merges by git, and git must see and keep the whole folder: a repository the
    /// files are committed in. The tool never commits for the team - the first commit is theirs.
    /// </summary>
    private async Task RequireAGitRepoAsync(string folder, CancellationToken ct)
    {
        if (await git.IsRepositoryAsync(folder, ct))
        {
            return;
        }

        throw new InvalidOperationException("The folder has files and merging needs git: move what must never reach history out (or list it in .gitignore), "
            + "then git init, a hook against secrets (gitleaks protect), git add -A and git commit - and run the command again. An empty folder needs no git at all.");
    }

    /// <summary>The manifest of a solution to add to: there, of this version, and the tree clean.</summary>
    private async Task<Manifest> LoadCheckedAsync(string root, Options options, CancellationToken ct)
    {
        var manifest = Manifest.Load(root)
            ?? throw new InvalidOperationException($"{Layout.ManifestPath} is missing: describe the solution first, dotskit init.");
        if (manifest.Upgrade is { } upgrade)
        {
            throw new InvalidOperationException($"An upgrade to {upgrade.To} goes a part at a time, {string.Join(", ", upgrade.Done)} merged: finish it, dotskit upgrade, then add.");
        }

        var version = Target;
        if (manifest.Template != version)
        {
            throw new InvalidOperationException(TemplateVersions.IsNewer(manifest.Template, version)
                ? $"The solution is of the template {manifest.Template}, newer than dotskit {version}: update the tool, dotnet tool update -g SawKing.DotsKit.Tool."
                : $"The solution is of the template {manifest.Template}, dotskit is {version}: bring the solution to {version} first, dotskit upgrade.");
        }

        await GitPreconditions.CheckAsync(git, root, options, ct);

        return manifest;
    }

    /// <summary>The manifest with the command's service: a new solution's first, or one more of the solution's.</summary>
    private Manifest Next(Manifest? current, TemplateArgs command)
    {
        var service = Ports.Assign(command.ServicePart(), current);
        if (current is null)
        {
            return new Manifest { Template = Target, Solution = command.SolutionPart().ToArgs(), Services = [] }.WithService(service);
        }

        RefuseOtherSolutionValues(current, command);
        var serviceToMerge = current.FindService(service.Service) is null ? service.WithInfrastructureOf(current.SolutionArgs) : service;
        return current.WithService(serviceToMerge);
    }

    /// <summary>A service takes the names, the database and the rest from the solution; a different one would break it.</summary>
    private static void RefuseOtherSolutionValues(Manifest current, TemplateArgs command)
    {
        var different = current.OtherSolutionValues(command);
        if (different.Count > 0)
        {
            throw new ArgumentException($"The solution has its own {string.Join(", ", different.Select(n => $"{n} ({current.SolutionArgs[n]})"))}: a service takes them from it, leave them out of the command.");
        }
    }
}
