using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;

namespace NamespaceRoot.ProductName.Common.Application.FeatureManagement;

public static class FeatureManagementServiceCollectionExtensions
{
    /// <summary>
    /// Registers the platform's feature flags: the catalogue and the library that evaluates them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One call per service, and nothing to declare — flags come from the shared file, so a service
    /// gains a new one without a line of code. What a service does add is a constant in
    /// <c>FeatureKeys</c> for a flag its own code reads.
    /// </para>
    /// <para>
    /// After this, two things work: <c>IFeatureCatalog</c> for the platform view with owners, expiry
    /// and value sources; <c>IFeatureManager</c> and <c>[FeatureGate]</c> from the library for
    /// evaluation.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddPlatformFeatureManagement(this IServiceCollection services)
    {
        services.TryAddSingleton<IFeatureCatalog, ConfigurationFeatureCatalog>();

        // The library evaluates; the catalogue is what it evaluates. Registered before
        // AddFeatureManagement so the built-in configuration-backed provider does not take the slot.
        services.TryAddSingleton<IFeatureDefinitionProvider, CatalogFeatureDefinitionProvider>();
        services.AddFeatureManagement();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PinnedFeaturesAnnouncer>());

        return services;
    }
}
