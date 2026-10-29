using NamespaceRoot.ProductName.Common.Application.FeatureManagement;
using NamespaceRoot.ProductName.Common.FeatureManagement;

namespace NamespaceRoot.ProductName.Common.Tests.FeatureManagement;

/// <summary>
/// A catalogue a test controls directly.
/// </summary>
/// <remarks>
/// <para>
/// State belongs to the instance, and the instance belongs to the test's <see cref="TestExecutionContext"/>,
/// so it disappears when the test does. That is the whole difference from keeping flags in a static
/// dictionary keyed by test name: nothing leaks between fixtures, and a parallel run is safe because
/// there is nothing shared to race over.
/// </para>
/// <para>
/// Only what a test needs is modelled — a key and whether it is on. Owner, expiry and tags exist for
/// people reading a list, and a test reading its own arrangement is not that.
/// </para>
/// </remarks>
public sealed class TestFeatureCatalog : IFeatureCatalog
{
    private readonly Dictionary<string, bool> _flags;

    /// <summary>
    /// Starts from whatever <see cref="WithFeatureAttribute"/> arranged for the running test, so a
    /// test that declared its features at the top does not have to repeat itself here.
    /// </summary>
    public TestFeatureCatalog() =>
        _flags = new Dictionary<string, bool>(WithFeatureAttribute.Arranged, StringComparer.OrdinalIgnoreCase);

    /// <summary>Declares a flag for this test, on unless said otherwise.</summary>
    public TestFeatureCatalog With(string key, bool enabled = true)
    {
        _flags[key] = enabled;
        return this;
    }

    /// <inheritdoc/>
    public bool IsEnabled(string key) => _flags.TryGetValue(key, out var enabled) && enabled;

    /// <inheritdoc/>
    public IReadOnlyList<FeatureState> GetAll() =>
        _flags.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => State(pair.Key, pair.Value))
            .ToList();

    /// <inheritdoc/>
    public FeatureState? Find(string key) =>
        _flags.TryGetValue(key, out var enabled) ? State(key, enabled) : null;

    private static FeatureState State(string key, bool enabled) => new()
    {
        Descriptor = new FeatureDescriptor { Key = key, Enabled = enabled },
        Enabled = enabled,
        Source = FeatureValueSource.File,
    };
}
