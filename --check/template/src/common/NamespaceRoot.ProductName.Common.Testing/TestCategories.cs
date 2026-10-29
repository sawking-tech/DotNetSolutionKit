namespace NamespaceRoot.ProductName.Common.Tests;

/// <summary>
/// Test categories, for running a kind of test alone or leaving it out:
/// <c>dotnet test --filter "TestCategory!=Integration"</c>.
/// </summary>
public static class TestCategories
{
    /// <summary>Needs a real PostgreSQL; see <see cref="Integration.Postgres"/>.</summary>
    public const string Integration = "Integration";

    /// <summary>
    /// The trait an xUnit test carries the category under; a service's tests set it with
    /// <c>[Integration]</c> from their <c>TestFramework.cs</c>. Named so that the filter CI uses for NUnit's
    /// categories, <c>TestCategory=Integration</c>, selects xUnit tests too.
    /// </summary>
    public const string TraitName = "TestCategory";
}
