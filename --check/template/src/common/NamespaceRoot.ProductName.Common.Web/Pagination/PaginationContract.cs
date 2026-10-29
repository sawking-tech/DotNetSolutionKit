using System.Reflection;
using NamespaceRoot.ProductName.Common.Domain.Querying;

namespace NamespaceRoot.ProductName.Common.Web.Pagination;

/// <summary>
/// The bounds of page and page size for HTTP requests: checked by <see cref="PaginationValidationFilter"/>
/// and published by <see cref="PaginationOperationFilter"/>, so the documentation and the check agree.
/// </summary>
public static class PaginationContract
{
    /// <summary>
    /// Largest page a request may ask for when its type declares no <see cref="PaginationLimitAttribute"/>.
    /// </summary>
    public const int DefaultMaximumPageSize = 1000;

    public static int MaximumPageSize(Type requestType)
        => requestType.GetCustomAttribute<PaginationLimitAttribute>()?.MaximumPageSize
           ?? DefaultMaximumPageSize;

    public static IReadOnlyList<PaginationError> Validate(IPaginationRequest request)
    {
        var errors = new List<PaginationError>(2);

        if (request.Page <= 0)
            errors.Add(new PaginationError(nameof(request.Page), "Page must be greater than 0."));

        if (request.PageSize <= 0)
            errors.Add(new PaginationError(nameof(request.PageSize), "PageSize must be greater than 0."));
        else
        {
            var maximum = MaximumPageSize(request.GetType());
            if (request.PageSize > maximum)
                errors.Add(new PaginationError(
                    nameof(request.PageSize), $"PageSize must be less than or equal to {maximum}."));
        }

        return errors;
    }
}

public sealed record PaginationError(string PropertyName, string Message);
