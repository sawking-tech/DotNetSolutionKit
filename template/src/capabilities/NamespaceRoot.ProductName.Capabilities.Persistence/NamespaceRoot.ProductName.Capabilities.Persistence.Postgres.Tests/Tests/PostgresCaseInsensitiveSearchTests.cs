using System.Linq.Expressions;
using NamespaceRoot.ProductName.Capabilities.Persistence.Postgres;

namespace NamespaceRoot.ProductName.Capabilities.Persistence.Postgres.Tests.Tests;

/// <summary>
/// The pattern the PostgreSQL implementation hands to ILIKE: escaping and wrapping are its contract
/// with the server, while what the search selects is covered by the in-memory double's tests.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class PostgresCaseInsensitiveSearchTests
{
    private sealed record Customer(string Name);

    [TestCase("smith", "%smith%", TestName = "A term is wrapped for a contains search")]
    [TestCase("50%", "%50/%%", TestName = "A percent sign is escaped")]
    [TestCase("a_b", "%a/_b%", TestName = "An underscore is escaped")]
    [TestCase("a/b", "%a//b%", TestName = "The escape character is escaped")]
    [TestCase("", "%", TestName = "An empty term matches everything")]
    [TestCase(" a ", "% a %", TestName = "Spaces around the term are kept")]
    public void Should_EscapeThePatternForIlike(string term, string expected)
    {
        var expression = new PostgresCaseInsensitiveSearch().GetSpecification<Customer>(c => c.Name, term).ToExpression();

        var ilike = (MethodCallExpression)expression.Body;
        ilike.Method.Name.ShouldBe("ILike");
        ((ConstantExpression)ilike.Arguments[2]).Value.ShouldBe(expected);
        ((ConstantExpression)ilike.Arguments[3]).Value.ShouldBe("/");
    }
}
