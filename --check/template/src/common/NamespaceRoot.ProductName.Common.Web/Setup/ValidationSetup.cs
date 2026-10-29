using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Web.Pagination;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Extensions;
using SharpGrip.FluentValidation.AutoValidation.Mvc.Results;

namespace NamespaceRoot.ProductName.Common.Web.Setup;

/// <summary>
/// FluentValidation for controllers: every validator in the given assemblies runs before the action,
/// and an invalid request is answered with 422 and <c>ValidationProblemDetails</c>. Model binding errors
/// and page bounds (<see cref="PaginationContract"/>) are answered the same way.
/// </summary>
public static class ValidationSetup
{
    /// <summary>
    /// Registers the validators found in <paramref name="assemblies"/> and validates action arguments
    /// with them automatically.
    /// </summary>
    /// <remarks>
    /// Validators are found by scanning rather than listed by hand: a validator that exists but is not
    /// registered fails silently, and the endpoint it guards simply stops rejecting bad input.
    /// </remarks>
    public static IServiceCollection AddValidation(this IServiceCollection services, params Assembly[] assemblies)
    {
        // Stop each rule chain at its first failure. FluentValidation defaults to Continue, which keeps
        // running the remaining conditions of the same RuleFor after an earlier one failed, so a chain
        // like NotNull().Must(list => list.Count > 0) dereferences the null it just rejected and throws
        // out of the validator itself: a 500 instead of a validation error.
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;

        services.AddValidatorsFromAssemblies(assemblies);
        services.AddFluentValidationAutoValidation(configuration =>
            configuration.OverrideDefaultResultFactoryWith<UnprocessableEntityResultFactory>());

        // ASP.NET Core answers invalid model state with 400; the platform answers every invalid
        // argument with one status, so a client handles one kind of input error.
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
            new UnprocessableEntityObjectResult(context.HttpContext.RequestServices
                .GetRequiredService<ProblemDetailsFactory>()
                .CreateValidationProblemDetails(
                    context.HttpContext, context.ModelState, StatusCodes.Status422UnprocessableEntity)));

        services.Configure<MvcOptions>(options => options.Filters.Add<PaginationValidationFilter>());
        return services;
    }

    /// <summary>
    /// Answers a failed validator with 422 instead of the 400 SharpGrip uses by default.
    /// </summary>
    /// <remarks>
    /// The problem is created again rather than given a new status: the one SharpGrip passes in already
    /// carries the type and title of a 400.
    /// </remarks>
    private sealed class UnprocessableEntityResultFactory : IFluentValidationAutoValidationResultFactory
    {
        public Task<IActionResult?> CreateActionResult(
            ActionExecutingContext context,
            ValidationProblemDetails validationProblemDetails,
            IDictionary<IValidationContext, ValidationResult> validationResults)
        {
            var problem = context.HttpContext.RequestServices
                .GetRequiredService<ProblemDetailsFactory>()
                .CreateValidationProblemDetails(
                    context.HttpContext, context.ModelState, StatusCodes.Status422UnprocessableEntity);
            return Task.FromResult<IActionResult?>(new UnprocessableEntityObjectResult(problem));
        }
    }
}
