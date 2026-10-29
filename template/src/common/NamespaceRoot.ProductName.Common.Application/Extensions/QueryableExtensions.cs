using System.Linq.Dynamic.Core;
using System.Reflection;
using NamespaceRoot.ProductName.Common.Domain.Querying;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Application.Extensions;

/// <summary>
/// Common extensions for IQueryable to handle sorting and pagination using standard interfaces.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Applies dynamic sorting based on property name and direction.
    /// Requires System.Linq.Dynamic.Core.
    /// Text columns are ordered case-insensitively so names interleave regardless of
    /// case instead of the database collation grouping all uppercase-initial names first.
    /// When <paramref name="request"/>.<c>SortBy</c> is provided but not present in
    /// <paramref name="mappings"/>, throws <see cref="BadRequestException"/> (→ HTTP 400).
    /// <para>
    /// <paramref name="defaultSort"/> is a trusted, complete Dynamic LINQ order expression used
    /// verbatim when no <c>SortBy</c> is supplied. It may carry its own direction and multiple
    /// columns (e.g. <c>"CreatedAt descending"</c>, <c>"Country.Name, NetworkName"</c>); the
    /// helper does NOT append a direction to it (doing so produced the invalid
    /// <c>"CreatedAt descending ascending"</c>, which Dynamic LINQ rejects).
    /// </para>
    /// <para>
    /// <paramref name="groupFirst"/> is a trusted Dynamic LINQ expression placed ahead of every
    /// other key, for lists that keep a fixed set of rows together regardless of which column the
    /// caller sorts by (e.g. pinned rows leading a list). The requested
    /// column still orders rows within each group.
    /// </para>
    /// </summary>
    public static IOrderedQueryable<T> ApplySorting<T>(
        this IQueryable<T> query,
        ISortableRequest? request,
        IReadOnlyDictionary<string, string> mappings,
        string defaultSort = "Id",
        string? groupFirst = null)
    {
        // No user-supplied sort: use the caller's trusted defaultSort expression as-is (it may
        // already encode direction / extra columns), only lower-casing a bare text column.
        if (request is null || string.IsNullOrWhiteSpace(request.SortBy))
            return query.OrderBy(
                Prepend(groupFirst, BuildOrderExpression(typeof(T), defaultSort, direction: null)));

        if (!mappings.TryGetValue(request.SortBy.ToLowerInvariant(), out var dbField))
        {
            var allowed = string.Join(", ", mappings.Keys);
            throw new BadRequestException(
                $"Invalid sortBy value '{request.SortBy}'. Allowed values: {allowed}.",
                "INVALID_SORT_FIELD");
        }

        var direction = request.SortDir == SortDirection.Desc ? "descending" : "ascending";
        return query.OrderBy(
            Prepend(groupFirst, BuildOrderExpression(typeof(T), dbField, direction)));
    }

    /// <summary>
    /// Puts <paramref name="groupFirst"/> ahead of the resolved sort so the grouping survives the
    /// user's column choice; the requested column then orders rows inside each group.
    /// </summary>
    private static string Prepend(string? groupFirst, string order) =>
        string.IsNullOrWhiteSpace(groupFirst) ? order : $"{groupFirst}, {order}";

    /// <summary>
    /// Builds a Dynamic LINQ order expression for a single mapped column. Text columns are ordered
    /// by their lower-cased value so "apple" interleaves with "Banana" instead of the DB collation
    /// grouping every uppercase-initial name first. A <c>null</c> <paramref name="direction"/>
    /// means <paramref name="field"/> is a trusted default expression and no direction is appended.
    /// </summary>
    /// <remarks>
    /// lower() bypasses a plain column index. A large list sorted by a text column needs a
    /// functional index on lower(column).
    /// </remarks>
    private static string BuildOrderExpression(Type type, string field, string? direction)
    {
        var expr = IsStringMember(type, field) ? $"{field}.ToLower()" : field;
        return direction is null ? expr : $"{expr} {direction}";
    }

    /// <summary>
    /// True when the dotted property path resolves to a <see cref="string"/> on
    /// <paramref name="rootType"/>. Unknown segments return false so an unexpected mapping falls
    /// back to raw ordering rather than throwing.
    /// </summary>
    private static bool IsStringMember(Type rootType, string path)
    {
        var type = rootType;
        foreach (var segment in path.Split('.'))
        {
            var property = type.GetProperty(
                segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null)
                return false;
            type = property.PropertyType;
        }

        return type == typeof(string);
    }

    /// <summary>
    /// Applies Skip and Take based on page number and page size.
    /// When <paramref name="request"/> is <c>null</c>, the query is returned unmodified (fetch all).
    /// </summary>
    public static IQueryable<T> ApplyPagination<T>(this IQueryable<T> query, IPaginationRequest? request)
    {
        if (request is null)
            return query;

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 10 : request.PageSize;

        return query
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
