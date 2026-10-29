using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using LinqSpecs;
using NamespaceRoot.ProductName.Common.Domain.Specifications;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Specifications;

namespace NamespaceRoot.ProductName.Common.Tests.Stubs;

/// <summary>
/// The in-memory stand-in for <see cref="PostgresCaseInsensitiveSearch"/>: the same escaped pattern, matched
/// the way PostgreSQL's <c>ILIKE</c> matches it.
/// </summary>
/// <remarks>
/// The pattern comes from <see cref="PostgresCaseInsensitiveSearch.EscapeAndWrapPattern"/> itself, so the
/// tests on the in-memory database see what the database would: a term with spaces around it keeps them,
/// and a change in the escaping reaches the tests too.
/// </remarks>
public sealed class InMemoryCaseInsensitiveSearch : ICaseInsensitiveSearch
{
    private static readonly MethodInfo IsMatchMethod =
        typeof(Regex).GetMethod(
            nameof(Regex.IsMatch),
            BindingFlags.Static | BindingFlags.Public,
            null,
            [typeof(string), typeof(string), typeof(RegexOptions)],
            null)!;

    private static readonly MethodInfo AnyMethod = typeof(Enumerable).GetMethods()
        .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(string));

    public Specification<T> GetSpecification<T>(Expression<Func<T, string>> propertyExpression, string? pattern)
    {
        var match = Match(propertyExpression.Body, pattern);
        return new AdHocSpecification<T>(Expression.Lambda<Func<T, bool>>(match, propertyExpression.Parameters[0]));
    }

    public Specification<T> GetArraySpecification<T>(Expression<Func<T, string[]>> arrayPropertyExpression, string? pattern)
    {
        var item = Expression.Parameter(typeof(string), "s");
        var itemMatch = Expression.Lambda<Func<string, bool>>(Match(item, pattern), item);
        var any = Expression.Call(null, AnyMethod, arrayPropertyExpression.Body, itemMatch);
        return new AdHocSpecification<T>(Expression.Lambda<Func<T, bool>>(any, arrayPropertyExpression.Parameters[0]));
    }

    // Singleline: % in ILIKE runs across line breaks, and so must .* here.
    private static MethodCallExpression Match(Expression value, string? pattern) =>
        Expression.Call(IsMatchMethod,
            value,
            Expression.Constant(LikePattern.ToRegex(PostgresCaseInsensitiveSearch.EscapeAndWrapPattern(pattern), '/')),
            Expression.Constant(RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));
}
