using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Web.Errors;

/// <summary>
/// Writes an exception that reached the top of the pipeline as a problem response.
/// </summary>
/// <remarks>
/// The body goes through <see cref="IProblemDetailsService"/>, the writer ASP.NET Core also uses for status
/// code pages, so every error a service returns has one shape.
/// </remarks>
internal sealed class GlobalExceptionHandler(
    PlatformExceptionMapper mapper,
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        Log(exception);

        var problem = mapper.Map(exception);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        if (exception is RateLimitException { RetryAfterSeconds: { } seconds })
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        // False only when the Accept header of the client rules out JSON; the client still gets the status.
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });

        return true;
    }

    /// <summary>
    /// Severity follows who is at fault: a rejected request is not an incident, and logging it as one
    /// teaches everybody to ignore the error log.
    /// </summary>
    private void Log(Exception exception)
    {
        switch (exception)
        {
            case OperationCanceledException:
                logger.LogInformation("The request was cancelled by the client or timed out");
                break;

            case BusinessLogicException or NotFoundException or RateLimitException or BadRequestException
                or FluentValidation.ValidationException:
                logger.LogWarning(exception, "The request was rejected");
                break;

            case JsonException:
                logger.LogWarning(exception, "The request body was not valid JSON");
                break;

            // A misconfigured service will not recover on a retry, so it gets its own level.
            case ConfigurationException:
                logger.LogCritical(exception, "The service is misconfigured");
                break;

            default:
                logger.LogError(exception, "An unhandled technical error occurred");
                break;
        }
    }
}
