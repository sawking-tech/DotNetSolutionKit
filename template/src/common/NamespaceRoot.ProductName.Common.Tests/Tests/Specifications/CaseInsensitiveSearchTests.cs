using NamespaceRoot.ProductName.Framework.Testing.Stubs;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Specifications;

/// <summary>
/// Case-insensitive search: what the in-memory double selects, which is what service tests rely on.
/// The pattern a provider hands to its server lives with the provider, in its capability's tests.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class CaseInsensitiveSearchTests
{
    private sealed record Customer(string Name, string[] Tags);

    private static readonly Customer[] Customers =
    [
        new("Anna Smith", ["vip", "Early-Adopter"]),
        new("BOB JONES", ["new"]),
        new("Discount 50% off", []),
        new("snake_case_name", []),
        new("path/to/file", []),
    ];

    private static string[] Search(string? term) =>
        Customers.AsQueryable()
            .Where(new InMemoryCaseInsensitiveSearch().GetSpecification<Customer>(c => c.Name, term).ToExpression())
            .Select(c => c.Name)
            .ToArray();

    [Test(Description = "A term matches anywhere in the value, regardless of case")]
    public void Should_MatchASubstringIgnoringCase()
    {
        Search("smith").ShouldBe(["Anna Smith"]);
        Search("bob").ShouldBe(["BOB JONES"]);
    }

    [Test(Description = "An empty term selects everything")]
    public void Should_SelectEverything_When_TheTermIsEmpty()
    {
        Search(null).Length.ShouldBe(Customers.Length);
        Search("").Length.ShouldBe(Customers.Length);
    }

    [TestCase("%", "Discount 50% off", TestName = "A percent sign is matched literally")]
    [TestCase("_case_", "snake_case_name", TestName = "An underscore is matched literally")]
    [TestCase("/to/", "path/to/file", TestName = "The escape character is matched literally")]
    public void Should_MatchWildcardsLiterally(string term, string expected) =>
        Search(term).ShouldBe([expected]);

    [Test(Description = "Spaces around the term are part of it, as in ILIKE: trimming is the caller's")]
    public void Should_KeepTheSpacesAroundTheTerm()
    {
        Search("smith ").ShouldBeEmpty();
        Search(" smith").ShouldBe(["Anna Smith"]);
    }

    [Test(Description = "A wildcard in the term does not widen the match")]
    public void Should_NotTreatAWildcardAsAWildcard() =>
        Search("a%s").ShouldBeEmpty();

    [Test(Description = "An array matches when any of its items contains the term")]
    public void Should_MatchAnyItemOfAnArray()
    {
        var spec = new InMemoryCaseInsensitiveSearch().GetArraySpecification<Customer>(c => c.Tags, "adopter");

        Customers.AsQueryable().Where(spec.ToExpression()).Select(c => c.Name).ShouldBe(["Anna Smith"]);
    }
}
