using System.Linq.Expressions;
using System.Reflection;
using LinqSpecs;
using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Domain.Specifications;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Specifications;

/// <summary>
/// SQL Server implementation of a case-insensitive "contains" search: <c>LIKE</c> under a case-insensitive
/// collation, whatever the collation of the column.
/// </summary>
/// <remarks>
/// SQL Server's <c>LIKE</c> treats <c>[</c> as the start of a character class, beside <c>%</c> and <c>_</c>, so
/// all three are escaped with <c>/</c>, as is <c>/</c> itself. The collation is a Windows one, which folds the
/// case of Unicode letters, not of Latin ones alone. An array of strings is a primitive collection, stored as
/// JSON and searched through <c>OPENJSON</c>.
/// </remarks>
public class SqlServerCaseInsensitiveSearch : ICaseInsensitiveSearch
{
    /// <summary>The collation the search compares under.</summary>
    public const string Collation = "Latin1_General_100_CI_AS";

    private static readonly MethodInfo LikeMethod = typeof(DbFunctionsExtensions).GetMethod(
                                                        nameof(DbFunctionsExtensions.Like),
                                                        [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])
                                                    ?? throw new InvalidOperationException("EF.Functions.Like with an escape character not found.");

    private static readonly MethodInfo CollateMethod = typeof(RelationalDbFunctionsExtensions)
                                                           .GetMethod(nameof(RelationalDbFunctionsExtensions.Collate))!
                                                           .MakeGenericMethod(typeof(string));

    public Specification<T> GetSpecification<T>(Expression<Func<T, string>> propertyExpression, string? pattern)
    {
        var parameter = propertyExpression.Parameters[0];
        var like = Like(propertyExpression.Body, EscapeAndWrapPattern(pattern));
        return new AdHocSpecification<T>(Expression.Lambda<Func<T, bool>>(like, parameter));
    }

    public Specification<T> GetArraySpecification<T>(Expression<Func<T, string[]>> arrayPropertyExpression, string? pattern)
    {
        var parameter = arrayPropertyExpression.Parameters[0];
        var item = Expression.Parameter(typeof(string), "s");
        var itemPredicate = Expression.Lambda<Func<string, bool>>(Like(item, EscapeAndWrapPattern(pattern)), item);

        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(string));
        var anyCall = Expression.Call(null, anyMethod, arrayPropertyExpression.Body, itemPredicate);
        return new AdHocSpecification<T>(Expression.Lambda<Func<T, bool>>(anyCall, parameter));
    }

    // EF.Functions.Like(EF.Functions.Collate(value, Collation), pattern, "/")
    private static MethodCallExpression Like(Expression value, string pattern)
    {
        var functions = Expression.Constant(null, typeof(DbFunctions));
        var collated = Expression.Call(null, CollateMethod, functions, value, Expression.Constant(Collation));
        return Expression.Call(null, LikeMethod, functions, collated, Expression.Constant(pattern), Expression.Constant("/"));
    }

    /// <summary>
    /// Escapes the wildcards of SQL Server's LIKE (<c>%</c>, <c>_</c>, <c>[</c>) with <c>/</c> and wraps the
    /// pattern for a "contains" search. Trimming or null-checking is the caller's.
    /// </summary>
    public static string EscapeAndWrapPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return "%";

        var escaped = pattern
            .Replace("/", "//")
            .Replace("%", "/%")
            .Replace("_", "/_")
            .Replace("[", "/[");

        return $"%{escaped}%";
    }
}
