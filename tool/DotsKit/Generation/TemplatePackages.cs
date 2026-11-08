namespace DotsKit.Generation;

/// <summary>
/// The opt-in local source of the template's package: the folder <c>DOTSKIT_TEMPLATE_PACKAGES</c> names, for runs
/// that must not reach nuget.org - an offline machine, a package built but not published. Only a published version
/// is looked up there: a release before nuget.org came from its tag on GitHub and still does.
/// </summary>
internal static class TemplatePackages
{
    /// <summary>The environment variable naming the folder with the template's nupkg files.</summary>
    public const string PackagesVariable = "DOTSKIT_TEMPLATE_PACKAGES";

    /// <summary>The folder the variable names, null when it names none.</summary>
    public static string? Folder => Environment.GetEnvironmentVariable(PackagesVariable) is { Length: > 0 } folder ? folder : null;

    /// <summary>The nupkg of <paramref name="packageId"/> of <paramref name="version"/> in <paramref name="folder"/>, when a folder is given.</summary>
    public static string? PackageIn(string? folder, string packageId, string version) =>
        folder is null ? null : Path.Combine(folder, $"{packageId}.{version}.nupkg");

    /// <summary>
    /// The package to install for a published version: the nupkg of the folder when the variable is set and the file
    /// is there, else the nuget.org form. A set variable without the file is a mistake worth naming, not a silent
    /// fall back to the network.
    /// </summary>
    public static string PackageOrNuGet(string? folder, string packageId, string version)
    {
        var package = PackageIn(folder, packageId, version);
        if (package is null)
        {
            return $"{packageId}::{version}";
        }

        return File.Exists(package) ? package : throw new InvalidOperationException(
            $"{packageId}.{version}.nupkg is not in {PackagesVariable} ({folder}): dotnet pack the template into the folder, or unset {PackagesVariable} to take the package from nuget.org.");
    }
}
