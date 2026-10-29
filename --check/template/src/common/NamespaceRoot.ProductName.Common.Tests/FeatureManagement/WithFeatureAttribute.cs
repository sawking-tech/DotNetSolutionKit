using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace NamespaceRoot.ProductName.Common.Tests.FeatureManagement;

/// <summary>
/// Runs a test with a feature in a stated state.
/// </summary>
/// <remarks>
/// <para>
/// Put it on a test or a fixture and the arrangement is visible where the test is read, instead of
/// buried in configuration set up three methods away:
/// </para>
/// <example>
/// <code>
/// [Test, WithFeature("checkout.new-flow")]
/// public async Task Should_serve_the_new_checkout() { … }
///
/// [Test, WithFeature("checkout.new-flow", false)]
/// public async Task Should_serve_the_old_one_when_off() { … }
/// </code>
/// </example>
/// <para>
/// State is held per test in an <see cref="AsyncLocal{T}"/> and cleared when the test ends, rather
/// than accumulated in a static dictionary keyed by test name. That difference matters under a
/// parallel run: there is nothing shared to race over, and nothing left behind for the next test to
/// inherit.
/// </para>
/// <para>
/// A <see cref="TestFeatureCatalog"/> created inside the test picks these up, so the arrangement
/// reaches the code under test without it knowing a test is running.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class WithFeatureAttribute(string featureKey, bool enabled = true) : Attribute, ITestAction
{
    private static readonly AsyncLocal<Dictionary<string, bool>?> Current = new();

    /// <summary>Features arranged for the test running right now.</summary>
    public static IReadOnlyDictionary<string, bool> Arranged =>
        Current.Value ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public ActionTargets Targets => ActionTargets.Test;

    /// <inheritdoc/>
    public void BeforeTest(ITest test)
    {
        Current.Value ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        Current.Value[featureKey] = enabled;
    }

    /// <inheritdoc/>
    public void AfterTest(ITest test) => Current.Value = null;
}
