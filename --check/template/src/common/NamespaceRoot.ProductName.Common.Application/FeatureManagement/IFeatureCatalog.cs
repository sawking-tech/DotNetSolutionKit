using JetBrains.Annotations;
using NamespaceRoot.ProductName.Common.FeatureManagement;

namespace NamespaceRoot.ProductName.Common.Application.FeatureManagement;

/// <summary>
/// The platform's feature flags: what is declared, and what each one currently evaluates to.
/// </summary>
/// <remarks>
/// <para>
/// Values are read at the moment of the call and never captured, so editing configuration takes
/// effect on a running service. Anything that caches a value in a field defeats the point of having
/// a flag at all.
/// </para>
/// <para>
/// This is the platform's own view, richer than a boolean: it carries owner, expiry, tags and the
/// layer a value came from, which is what a management UI needs. Code that only asks "is this on"
/// can use <see cref="IsEnabled"/>, or <c>IFeatureManager</c> from the library, which is backed by
/// the same catalogue.
/// </para>
/// </remarks>
[PublicAPI]
public interface IFeatureCatalog
{
    /// <summary>
    /// Whether the feature is on right now. An undeclared key is <c>false</c>: asking about a feature
    /// this platform never declared is asking about something that is not here.
    /// </summary>
    bool IsEnabled(string key);

    /// <summary>Every declared feature with its current value, source and expiry, ordered by key.</summary>
    IReadOnlyList<FeatureState> GetAll();

    /// <summary>One feature, or <c>null</c> when nothing declares that key.</summary>
    FeatureState? Find(string key);
}
