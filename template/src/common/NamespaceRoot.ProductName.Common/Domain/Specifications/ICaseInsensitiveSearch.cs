using System.Linq.Expressions;
using LinqSpecs;

namespace NamespaceRoot.ProductName.Common.Domain.Specifications;

/// <summary>
/// Provides methods to build specifications for case-insensitive search
/// optimized for the underlying database provider (e.g., PostgreSQL ILIKE).
/// </summary>
public interface ICaseInsensitiveSearch
{
    /// <summary>
    /// Builds a specification for a case-insensitive "contains" search on a string property.
    /// Supports nested properties: x => x.User.Email
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="propertyExpression">Expression pointing to the string property.</param>
    /// <param name="pattern">Search string (will be escaped and wrapped in % internally).</param>
    Specification<T> GetSpecification<T>(Expression<Func<T, string>> propertyExpression, string? pattern);

    /// <summary>
    /// Builds a specification for a case-insensitive search within a string array/collection.
    /// Optimized for PostgreSQL string[] columns.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="arrayPropertyExpression">Expression pointing to the string array property.</param>
    /// <param name="pattern">Search string (will be escaped and wrapped in % internally).</param>
    Specification<T> GetArraySpecification<T>(Expression<Func<T, string[]>> arrayPropertyExpression, string? pattern);
}
