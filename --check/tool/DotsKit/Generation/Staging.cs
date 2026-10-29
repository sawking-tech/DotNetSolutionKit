using DotsKit.Io;
using DotsKit.Solutions;

namespace DotsKit.Generation;

/// <summary>
/// Builds in a folder of its own what the template gives for a manifest, the way a person would with the
/// template alone: the solution with its first service, then every service generated into it, each adding
/// itself to All.sln by the template's post-action. The first service is generated again by its own
/// arguments, since the solution's command made it with the solution's. A version before that post-action
/// added nothing to All.sln, so what it left out is added here, as its script did.
/// </summary>
internal interface IStaging
{
    /// <summary>A folder with what the template gives for <paramref name="manifest"/>.</summary>
    /// <param name="manifest">The solution to generate: its version, the solution's arguments and each service's.</param>
    /// <param name="withSolutionFile">
    /// All.sln is made right too, for a new solution that takes it as it is; a solution there already changes its
    /// own by the projects of the two trees (<see cref="SolutionProjects.InTree"/>), and the work is saved.
    /// </param>
    /// <param name="ct">Cancels the generation.</param>
    Task<string> BuildAsync(Manifest manifest, bool withSolutionFile, CancellationToken ct);
}

internal sealed class Staging(ITemplateSource templates, ISolutionProjects projects) : IStaging
{
    public async Task<string> BuildAsync(Manifest manifest, bool withSolutionFile, CancellationToken ct)
    {
        var template = templates.Of(manifest.Template);
        var root = Path.Combine(Folders.NewTemp("staging"), "solution");
        var services = manifest.ServiceArgs;
        await template.GenerateAsync(TemplateArgs.ForSolution(manifest.SolutionArgs, services[0], manifest.Template).ForVersion(manifest.Template), root, ct);
        RemoveServiceFolders(root);
        // The solution's command listed its first service as a regular one; a gateway in its place has fewer projects.
        if (withSolutionFile)
        {
            await projects.RemoveMissingAsync(root, ct);
        }

        foreach (var service in services)
        {
            await template.GenerateAsync(TemplateArgs.ForService(manifest.SolutionArgs, service).ForVersion(manifest.Template), root, ct);
            // Before 2.8 the template left this to manual-add-projects.sh. Goes in 3.0
            // (TemplateVersions.AddsServiceToSolution).
            if (withSolutionFile && !TemplateVersions.AddsServiceToSolution(manifest.Template))
            {
                await projects.AddMissingAsync(root, ct);
            }
        }
        return root;
    }

    private static void RemoveServiceFolders(string root)
    {
        foreach (var folder in Directory.GetDirectories(Layout.ServicesOf(root)))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
