namespace NamespaceRoot.ProductName.Common.Contracts.Responses;

/// <summary>
/// Represents a complete paginated API response combining both items and pagination metadata.
/// This is the primary interface for all paginated API responses in the system.
/// </summary>
/// <typeparam name="T">Type of the items in the collection.</typeparam>
public interface IPaginatedResponse<out T> : IItemsResponse<T>, IPaginationResponse
{
    // Inherits both Items from IItemsResponse<T> and pagination properties from IPaginationResponse
}

/// <summary>
/// Represents pagination metadata for API responses.
/// Contains information about the current page, page size, and total counts.
/// </summary>
public interface IPaginationResponse
{
    /// <summary>
    /// Gets the current page number (1-based index).
    /// </summary>
    int Page { get; }

    /// <summary>
    /// Gets the number of items per page.
    /// </summary>
    int PageSize { get; }

    /// <summary>
    /// Gets the total number of items across all pages.
    /// </summary>
    int TotalCount { get; }

    /// <summary>
    /// Gets the total number of pages available.
    /// </summary>
    int TotalPages { get; }
}
