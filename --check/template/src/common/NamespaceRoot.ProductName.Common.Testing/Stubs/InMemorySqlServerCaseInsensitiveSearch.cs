using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using LinqSpecs;
using NamespaceRoot.ProductName.Common.Domain.Specifications;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Specifications;

namespace NamespaceRoot.ProductName.Common.Tests.Stubs;

/// <summary>
/// The in-memory stand-in for <see cref="SqlServerCaseInsensitiveSearch"/>: the same escaped pattern, matched
/// the way SQL Server's <c>LIKE</c> under a case-insensitive collation matches it.
/// </summary>
/// <remarks>
/// The pattern comes from <see cref="SqlServerCaseInsensitiveSearch.EscapeAndWrapPattern"/> itself, so a
/// change there reaches the tests on the in-memory database too. <c>[</c> is escaped there, so a character
/// class never reaches the match.
/// </remarks>
public sealed class InMemorySqlServerCaseInsensitiveSearch : ICaseInsensitiveSearch
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

    private static MethodCallExpression Match(Expression value, string? pattern) =>
        Expression.Call(IsMatchMethod,
            value,
            Expression.Constant(LikePattern.ToRegex(SqlServerCaseInsensitiveSearch.EscapeAndWrapPattern(pattern), '/')),
            Expression.Constant(RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));
}
