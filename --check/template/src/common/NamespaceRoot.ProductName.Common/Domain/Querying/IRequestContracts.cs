namespace NamespaceRoot.ProductName.Common.Domain.Querying;

/// <summary>
/// Supports keyword-based searching.
/// </summary>
public interface ISearchableRequest
{
    string? Search { get; init; }
}

/// <summary>
/// Supports data sorting.
/// </summary>
public interface ISortableRequest
{
    string? SortBy { get; init; }
    SortDirection SortDir { get; init; }
}

/// <summary>
/// Supports pagination.
/// </summary>
public interface IPaginationRequest
{
    int Page { get; init; }
    int PageSize { get; init; }
}
