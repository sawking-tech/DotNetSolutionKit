using Microsoft.OpenApi.Models;
using NamespaceRoot.ProductName.Common.Domain.Querying;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Web.Pagination;

/// <summary>
/// Publishes in the OpenAPI document the bounds that <see cref="PaginationValidationFilter"/> enforces.
/// </summary>
// TODO: Web/Pagination stays in Common.Web although the 3.0 migration map moved this filter to
// Framework.OpenApi - the filter reads PaginationContract, Common.Web references Framework.OpenApi, and
// the move closes the cycle. ADR-009 allows a violation with its reason written; this is the reason.
// The bounds are the team's product format, not a Swagger concern.
public sealed class PaginationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.GetCustomAttributes(true).OfType<SkipPaginationValidationAttribute>().Any())
            return;

        var requestType = context.MethodInfo.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .FirstOrDefault(type => typeof(IPaginationRequest).IsAssignableFrom(type));

        if (requestType is null)
            return;

        SetBounds(operation, nameof(IPaginationRequest.Page), 1, null);
        SetBounds(operation, nameof(IPaginationRequest.PageSize), 1, PaginationContract.MaximumPageSize(requestType));
    }

    private static void SetBounds(OpenApiOperation operation, string property, decimal minimum, decimal? maximum)
    {
        var parameter = operation.Parameters.FirstOrDefault(candidate =>
            candidate.Name.Equals(property, StringComparison.OrdinalIgnoreCase));
        if (parameter is null)
            return;

        parameter.Schema.Minimum = minimum;
        parameter.Schema.Maximum = maximum;
    }
}
