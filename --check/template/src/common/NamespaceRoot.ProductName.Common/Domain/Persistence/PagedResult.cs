namespace NamespaceRoot.ProductName.Common.Domain.Persistence;

/// <summary>
/// One page of a larger result, together with the size of the whole.
/// </summary>
/// <param name="Items">The entities on this page.</param>
/// <param name="TotalCount">How many entities satisfy the query in total, ignoring paging.</param>
/// <param name="Page">The 1-based page number this result was read from.</param>
/// <param name="PageSize">How many entities a full page holds.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>
    /// How many pages the result spans. Zero when the query matched nothing.
    /// </summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>
    /// An empty page, used when a query matches nothing.
    /// </summary>
    public static PagedResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}
