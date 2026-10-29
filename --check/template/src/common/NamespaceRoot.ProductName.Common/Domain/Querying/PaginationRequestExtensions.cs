namespace NamespaceRoot.ProductName.Common.Domain.Querying;

/// <summary>
/// Reading a paging request the same way everywhere.
/// </summary>
/// <remarks>
/// A request arrives from the outside and may carry nothing, a zero or a negative number. Normalising it
/// at every call site is how the query ends up paging by one set of numbers while the response reports
/// another, so both sides go through here.
/// </remarks>
public static class PaginationRequestExtensions
{
    /// <summary>
    /// Page size used when the request does not ask for one.
    /// </summary>
    public const int DefaultPageSize = 10;

    /// <summary>
    /// The page to read, 1-based. Anything below the first page reads the first page.
    /// </summary>
    public static int NormalisedPage(this IPaginationRequest request) =>
        request.Page <= 0 ? 1 : request.Page;

    /// <summary>
    /// How many entities the page holds, falling back to <see cref="DefaultPageSize"/>.
    /// </summary>
    public static int NormalisedPageSize(this IPaginationRequest request) =>
        request.PageSize <= 0 ? DefaultPageSize : request.PageSize;
}
