using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Domain.Querying;

namespace NamespaceRoot.ProductName.Common.Web.Pagination;

/// <summary>
/// Rejects a page or page size outside <see cref="PaginationContract"/> before the action runs.
/// </summary>
/// <remarks>
/// The answer is the same 422 validation problem as any other invalid argument, so a client handles one
/// kind of input error. Without the check, a page size of a million reaches the database.
/// </remarks>
public sealed class PaginationValidationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<SkipPaginationValidationAttribute>().Any())
            return;

        foreach (var request in context.ActionArguments.Values.OfType<IPaginationRequest>())
        foreach (var error in PaginationContract.Validate(request))
            context.ModelState.AddModelError(error.PropertyName, error.Message);

        if (context.ModelState.IsValid)
            return;

        var problem = context.HttpContext.RequestServices
            .GetRequiredService<ProblemDetailsFactory>()
            .CreateValidationProblemDetails(context.HttpContext, context.ModelState, StatusCodes.Status422UnprocessableEntity);

        context.Result = new UnprocessableEntityObjectResult(problem);
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}

/// <summary>
/// Marks an action that takes a type with page and page size properties but does not page, so its
/// arguments are not checked against the pagination bounds.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SkipPaginationValidationAttribute : Attribute;
