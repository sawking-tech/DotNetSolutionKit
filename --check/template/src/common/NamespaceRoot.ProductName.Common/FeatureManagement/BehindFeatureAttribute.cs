using JetBrains.Annotations;

namespace NamespaceRoot.ProductName.Common.FeatureManagement;

/// <summary>
/// Marks code that exists only because a feature flag does.
/// </summary>
/// <remarks>
/// <para>
/// This is not how a flag is evaluated — that is <c>IFeatureManager</c> in code and
/// <c>[FeatureGate]</c> on an endpoint. This says something different and longer-lived: the class,
/// the method, the property carrying it are there for the flag, and go when the flag goes.
/// </para>
/// <para>
/// Retiring a flag always ends in the same question — what can be deleted. Searching for the key
/// finds the places that read it, not the code that exists for it, so the answer is usually pieced
/// together from memory. The marker turns that into a search.
/// </para>
/// <para>
/// It reads in the other direction too: marked code being lifted out and reused elsewhere is a sign
/// the flag has already done its job and is now only ceremony.
/// </para>
/// <example>
/// <code>
/// [BehindFeature("provider.v2")]
/// public sealed class V2ProviderAdapter : IProviderAdapter { … }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field |
    AttributeTargets.Enum | AttributeTargets.Constructor,
    AllowMultiple = true,
    Inherited = false)]
[PublicAPI]
public sealed class BehindFeatureAttribute(string featureKey) : Attribute
{
    /// <summary>Key of the feature this code belongs to. Must be a declared flag.</summary>
    public string FeatureKey { get; } = featureKey;

    /// <summary>
    /// What happens to this code when the flag is retired. Defaults to removal, which is the usual
    /// answer; set it to <c>false</c> for code that stays and merely stops being conditional.
    /// </summary>
    public bool RemoveWithFeature { get; init; } = true;
}
