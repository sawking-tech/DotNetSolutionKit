using System.Linq.Expressions;
using System.Reflection;
using NamespaceRoot.ProductName.Common.Domain.Specifications;
using LinqSpecs;
using Microsoft.EntityFrameworkCore;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Specifications;

/// <summary>
/// PostgreSQL implementation for case-insensitive search using Npgsql ILIKE.
/// Designed to work with LinqSpecs. Single implementation reused by every service
/// (formerly copy-pasted per service).
/// </summary>
public class PostgresCaseInsensitiveSearch : ICaseInsensitiveSearch
{
    private static readonly MethodInfo ILikeMethod = typeof(NpgsqlDbFunctionsExtensions).GetMethod(
                                                         nameof(NpgsqlDbFunctionsExtensions.ILike),
                                                         [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])
                                                     ?? throw new InvalidOperationException("Npgsql ILike method not found.");

    /// <summary>
    /// Builds a specification for a "contains" search on a string property.
    /// Supports nested properties like x => x.Sub.Property.
    /// </summary>
    public Specification<T> GetSpecification<T>(Expression<Func<T, string>> propertyExpression, string? pattern)
    {
        var searchTerm = EscapeAndWrapPattern(pattern);

        var parameter = propertyExpression.Parameters[0];
        var propertyAccess = propertyExpression.Body;

        // Represents: EF.Functions.ILike(property, "%pattern%", "/")
        var ilikeCall = Expression.Call(
            null,
            ILikeMethod,
            Expression.Constant(null, typeof(DbFunctions)),
            propertyAccess,
            Expression.Constant(searchTerm),
            Expression.Constant("/")
        );

        var lambda = Expression.Lambda<Func<T, bool>>(ilikeCall, parameter);
        return new AdHocSpecification<T>(lambda);
    }

    /// <summary>
    /// Builds a specification for searching a substring within a PostgreSQL string array (string[]).
    /// Represents: x => x.ArrayProperty.Any(s => EF.Functions.ILike(s, "%pattern%", "/"))
    /// </summary>
    public Specification<T> GetArraySpecification<T>(Expression<Func<T, string[]>> arrayPropertyExpression, string? pattern)
    {
        var searchTerm = EscapeAndWrapPattern(pattern);
        var parameter = arrayPropertyExpression.Parameters[0];

        // Inner lambda for .Any(): s => EF.Functions.ILike(s, searchTerm, "/")
        var itemParam = Expression.Parameter(typeof(string), "s");
        var ilikeCall = Expression.Call(
            null,
            ILikeMethod,
            Expression.Constant(null, typeof(DbFunctions)),
            itemParam,
            Expression.Constant(searchTerm),
            Expression.Constant("/")
        );
        var itemPredicate = Expression.Lambda<Func<string, bool>>(ilikeCall, itemParam);

        // Resolve Enumerable.Any<string>(IEnumerable<string>, Func<string, bool>)
        var anyMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(string));

        var anyCall = Expression.Call(null, anyMethod, arrayPropertyExpression.Body, itemPredicate);

        var lambda = Expression.Lambda<Func<T, bool>>(anyCall, parameter);
        return new AdHocSpecification<T>(lambda);
    }

    /// <summary>
    /// Escapes SQL wildcard characters (_, %) and wraps the pattern for a "contains" search.
    /// Responsibility for trimming or null-checking business logic lies with the caller.
    /// </summary>
    public static string EscapeAndWrapPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return "%";

        // '/' is used as the ESCAPE character.
        // We must escape:
        // 1. The escape character itself '/' -> '//'
        // 2. The any-sequence wildcard '%' -> '/%'
        // 3. The single-character wildcard '_' -> '/_'
        var escaped = pattern
            .Replace("/", "//")
            .Replace("%", "/%")
            .Replace("_", "/_");

        return $"%{escaped}%";
    }
}
