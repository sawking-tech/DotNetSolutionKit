using Microsoft.FeatureManagement;

namespace NamespaceRoot.ProductName.Common.Application.FeatureManagement;

/// <summary>
/// Feeds <c>Microsoft.FeatureManagement</c> from the platform catalogue.
/// </summary>
/// <remarks>
/// <para>
/// The library owns evaluation — <c>IFeatureManager</c>, <c>[FeatureGate]</c>, per-request snapshots —
/// and none of that is worth writing again. It only needs to be told what the features are, which is
/// this class: the catalogue answers with the platform's own schema, and each entry is handed over as
/// a definition the library understands.
/// </para>
/// <para>
/// The translation is deliberately blunt. Our flags are on or off once the environment has been taken
/// into account, so an enabled feature arrives as always-on and a disabled one as a definition with no
/// filters. The library's own filters — percentages, time windows, targeting — are not used, because
/// the environment override already covers the case we have, and the rest is machinery nobody asked
/// for.
/// </para>
/// <para>
/// Definitions are built per call rather than cached, so a flag edited in configuration takes effect
/// on a running service through the library as well, not only through the catalogue.
/// </para>
/// </remarks>
public sealed class CatalogFeatureDefinitionProvider : IFeatureDefinitionProvider
{
    /// <summary>Filter name the library treats as "on, unconditionally".</summary>
    private const string AlwaysOn = "AlwaysOn";

    private readonly IFeatureCatalog _catalog;

    public CatalogFeatureDefinitionProvider(IFeatureCatalog catalog) => _catalog = catalog;

    /// <inheritdoc/>
    public Task<FeatureDefinition> GetFeatureDefinitionAsync(string featureName)
    {
        var state = _catalog.Find(featureName);

        // An unknown feature is off rather than an error: asking about something this platform never
        // declared is asking about something that is not here.
        return Task.FromResult(Define(featureName, state?.Enabled ?? false));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<FeatureDefinition> GetAllFeatureDefinitionsAsync()
    {
        foreach (var state in _catalog.GetAll())
            yield return Define(state.Key, state.Enabled);

        await Task.CompletedTask;
    }

    private static FeatureDefinition Define(string name, bool enabled) => new()
    {
        Name = name,
        EnabledFor = enabled
            ? [new FeatureFilterConfiguration { Name = AlwaysOn }]
            : [],
    };
}
