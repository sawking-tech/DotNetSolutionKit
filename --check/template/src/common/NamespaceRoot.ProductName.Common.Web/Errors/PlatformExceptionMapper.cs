using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using NamespaceRoot.ProductName.Common.Contracts.Responses;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Web.Errors;

/// <summary>
/// Turns an exception into the RFC 9457 problem every service answers with: the status, a title, the
/// detail a caller can act on, and a <c>code</c> extension that client code matches on.
/// </summary>
/// <remarks>
/// Kept free of <c>HttpContext</c>, so what an exception becomes is tested without a request. The
/// correlation and trace identifiers are added when the problem is written.
/// </remarks>
public sealed class PlatformExceptionMapper
{
    /// <summary>
    /// The extension carrying the machine-readable error code.
    /// </summary>
    public const string CodeExtension = "code";

    /// <summary>
    /// Status for a request the client abandoned. It is not a server error, so it stays out of the 5xx
    /// rate that alerting watches.
    /// </summary>
    public const int ClientClosedRequest = 499;

    private readonly IReadOnlyList<IExceptionMapping> _serviceMappings;
    private readonly bool _revealTechnicalDetail;

    /// <param name="serviceMappings">
    /// Service rules, consulted first, so a service can add cases or override a default.
    /// </param>
    /// <param name="revealTechnicalDetail">
    /// Whether the message of an unexpected exception reaches the client. True only outside production:
    /// such a message often names a table, a host or a file path.
    /// </param>
    public PlatformExceptionMapper(IEnumerable<IExceptionMapping> serviceMappings, bool revealTechnicalDetail)
    {
        _serviceMappings = serviceMappings.ToList();
        _revealTechnicalDetail = revealTechnicalDetail;
    }

    public ProblemDetails Map(Exception exception)
    {
        foreach (var mapping in _serviceMappings)
        {
            var mapped = mapping.TryMap(exception);
            if (mapped is not null)
            {
                return mapped;
            }
        }

        return MapKnown(exception);
    }

    /// <summary>
    /// A problem with the given status, detail and code. The title is the reason phrase of the status.
    /// </summary>
    public static ProblemDetails Problem(int status, string? detail, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = TitleOf(status), Detail = detail };
        problem.Extensions[CodeExtension] = code;
        return problem;
    }

    /// <summary>
    /// A problem listing the rejected fields, in the shape ASP.NET Core uses for model validation. 422 by
    /// default, like every validation failure of the platform.
    /// </summary>
    public static HttpValidationProblemDetails ValidationProblem(
        IDictionary<string, string[]> errors,
        string? detail = null,
        string code = ErrorConstants.Codes.ValidationError,
        int status = StatusCodes.Status422UnprocessableEntity)
    {
        var problem = new HttpValidationProblemDetails(errors)
        {
            Status = status,
            Title = TitleOf(status),
            Detail = detail,
        };
        problem.Extensions[CodeExtension] = code;
        return problem;
    }

    private ProblemDetails MapKnown(Exception exception) => exception switch
    {
        OperationCanceledException => Problem(
            ClientClosedRequest, ErrorConstants.Messages.RequestCancelled, ErrorConstants.Codes.RequestCancelled),

        JsonException ex => Problem(
            StatusCodes.Status400BadRequest,
            $"{ErrorConstants.Messages.MalformedJson} {ex.Message}",
            ErrorConstants.Codes.BadRequest),

        // A request asking for something the endpoint does not offer, such as an unknown sort field. The
        // message names what is available, so it is passed through.
        BadRequestException ex => Problem(
            StatusCodes.Status400BadRequest, ex.Message, ex.ErrorCode ?? ErrorConstants.Codes.BadRequest),

        FluentValidation.ValidationException ex => ValidationProblem(
            ex.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray()),
            ErrorConstants.Messages.ValidationFailed),

        // The rejected parameter is named as a field error, so the caller learns which value to fix.
        InconsistentDataException { ParameterName: { } parameter } ex => ValidationProblem(
            new Dictionary<string, string[]> { [parameter] = [ex.Message] },
            ex.Message,
            ErrorConstants.Codes.BadRequest,
            StatusCodes.Status400BadRequest),

        InconsistentDataException ex => Problem(
            StatusCodes.Status400BadRequest, ex.Message, ErrorConstants.Codes.BadRequest),

        SecurityTokenException ex => Problem(
            StatusCodes.Status401Unauthorized, ex.Message, ErrorConstants.Codes.InvalidToken),

        UnauthorizedAccessException ex => Problem(
            StatusCodes.Status401Unauthorized, ex.Message, ErrorConstants.Codes.Unauthorized),

        // Derived business exceptions come before their base: the base arm would answer 422 for all of
        // them, and the client would lose the difference between "forbidden" and "invalid".
        AccessDeniedException ex => Problem(
            StatusCodes.Status403Forbidden, ex.Message, ex.ErrorCode ?? ErrorConstants.Codes.Forbidden),

        NotFoundException ex => Problem(
            StatusCodes.Status404NotFound, ex.Message, ErrorConstants.Codes.NotFound),

        // The internal message names row versions; the caller only needs to reload.
        ConcurrencyException ex => Problem(
            StatusCodes.Status409Conflict,
            ErrorConstants.Messages.ConcurrencyConflict,
            ex.ErrorCode ?? ErrorConstants.Codes.Conflict),

        ConflictException ex => Problem(
            StatusCodes.Status409Conflict, ex.Message, ex.ErrorCode ?? ErrorConstants.Codes.Conflict),

        UniqueViolationException ex => Problem(
            StatusCodes.Status409Conflict, ex.Message, ex.ErrorCode ?? ErrorConstants.Codes.Conflict),

        RequestDuplicationException ex => Problem(
            StatusCodes.Status409Conflict, ex.Message, ex.ErrorCode ?? ErrorConstants.Codes.Conflict),

        RateLimitException ex => Problem(
            StatusCodes.Status429TooManyRequests, ex.Message, ErrorConstants.Codes.RateLimit),

        BusinessLogicException ex => Problem(
            StatusCodes.Status422UnprocessableEntity, ex.Message, ex.ErrorCode ?? ErrorConstants.Codes.ValidationError),

        ValidationException ex => Problem(
            StatusCodes.Status422UnprocessableEntity, ex.Message, ErrorConstants.Codes.ValidationError),

        ServiceUnavailableException ex => Problem(
            StatusCodes.Status503ServiceUnavailable,
            ex.Message,
            ex.ErrorCode ?? ErrorConstants.Codes.ServiceUnavailable),

        // The message of a misconfigured service names secrets, hosts and paths. It never reaches the
        // client, not even outside production.
        ConfigurationException => Problem(
            StatusCodes.Status500InternalServerError,
            ErrorConstants.Messages.InternalServerError,
            ErrorConstants.Codes.InternalError),

        _ => Problem(
            StatusCodes.Status500InternalServerError,
            _revealTechnicalDetail ? exception.Message : ErrorConstants.Messages.InternalServerError,
            ErrorConstants.Codes.InternalError),
    };

    private static string TitleOf(int status) =>
        status == ClientClosedRequest ? "Client Closed Request" : ReasonPhrases.GetReasonPhrase(status);
}
