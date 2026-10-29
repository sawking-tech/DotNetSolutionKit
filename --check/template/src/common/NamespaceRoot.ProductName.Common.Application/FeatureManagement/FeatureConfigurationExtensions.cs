using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace NamespaceRoot.ProductName.Common.Application.FeatureManagement;

/// <summary>
/// Puts the platform's feature file into a service's configuration.
/// </summary>
public static class FeatureConfigurationExtensions
{
    /// <summary>Name of the shared file, as it lands in the build output.</summary>
    public const string FileName = "features.json";

    /// <summary>
    /// Adds the shared feature file as the bottom configuration layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file ships from <c>Common</c> and travels with the project reference, so it lands next to the
    /// assemblies rather than in the project directory. A plain relative path would not find it there:
    /// configuration resolves relative paths against the content root, which is the project directory
    /// under <c>dotnet run</c> and the application directory in a container. Anchoring the provider to
    /// the base directory makes both agree, instead of working in one and silently reading nothing in
    /// the other.
    /// </para>
    /// <para>
    /// Optional on purpose: a service started without the file answers <c>false</c> to every flag, which
    /// is the safe direction. <c>reloadOnChange</c> is what lets an edit reach a running service.
    /// </para>
    /// </remarks>
    public static IConfigurationBuilder AddPlatformFeatures(this IConfigurationBuilder builder) =>
        builder.AddJsonFile(
            new PhysicalFileProvider(AppContext.BaseDirectory),
            FileName,
            optional: true,
            reloadOnChange: true);
}
