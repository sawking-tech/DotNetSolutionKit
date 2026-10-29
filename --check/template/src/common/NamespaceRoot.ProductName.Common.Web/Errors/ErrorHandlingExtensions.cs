using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Contracts.Responses;
using NamespaceRoot.ProductName.Common.Web.Tracing;

namespace NamespaceRoot.ProductName.Common.Web.Errors;

/// <summary>
/// Correlation and error handling for a service host. Every service calls these, so two services cannot
/// answer the same failure differently.
/// </summary>
public static class ErrorHandlingExtensions
{
    /// <summary>
    /// The problem extension carrying the correlation identifier of the request.
    /// </summary>
    public const string CorrelationIdExtension = "correlationId";

    /// <summary>
    /// The problem extension carrying the W3C trace identifier, under the name ASP.NET Core MVC uses.
    /// </summary>
    public const string TraceIdExtension = "traceId";

    /// <summary>
    /// Registers the correlation context, the exception handler and the RFC 9457 problem writer.
    /// </summary>
    /// <remarks>
    /// Every problem the service writes, from an exception, a status code page or model validation, gets
    /// the correlation identifier, and the field names in <c>errors</c> follow the JSON naming policy of
    /// the response. A service adds its own exception rules by registering <see cref="IExceptionMapping"/>
    /// implementations.
    /// </remarks>
    public static IServiceCollection AddPlatformErrorHandling(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton<ICorrelationContext, HttpCorrelationContext>();

        services.AddSingleton(provider => new PlatformExceptionMapper(
            provider.GetServices<IExceptionMapping>(),
            revealTechnicalDetail: !environment.IsProduction()));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var requestServices = context.HttpContext.RequestServices;

            var correlationId = requestServices.GetService<ICorrelationContext>()?.CorrelationId;
            if (!string.IsNullOrEmpty(correlationId))
            {
                context.ProblemDetails.Extensions[CorrelationIdExtension] = correlationId;
            }

            // MVC puts the trace identifier on the problems it creates; the writer behind exceptions and
            // status code pages does not. Added here by the same rule, so every problem carries it.
            context.ProblemDetails.Extensions.TryAdd(
                TraceIdExtension, Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

            if (context.ProblemDetails is HttpValidationProblemDetails validation)
            {
                // Model validation, the validators and the pagination check create these without a
                // code; a client matches every invalid argument on the same one.
                validation.Extensions.TryAdd(PlatformExceptionMapper.CodeExtension, ErrorConstants.Codes.ValidationError);

                var naming = requestServices.GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()?
                    .Value.SerializerOptions.PropertyNamingPolicy;
                RenameFields(validation.Errors, naming);
            }
        });

        return services;
    }

    /// <summary>
    /// Puts the correlation and trace identifiers on the request and on every log line it produces.
    /// </summary>
    /// <remarks>
    /// Goes first in the pipeline, before request logging. Registered after it, the request log line, the
    /// one with method, path, status and duration, is written outside the scope and carries no identifier.
    /// </remarks>
    public static WebApplication UsePlatformTracing(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();

        return app;
    }

    /// <summary>
    /// Answers anything thrown further down the pipeline, and any empty error response such as an
    /// unmatched route, with a problem.
    /// </summary>
    /// <remarks>
    /// Goes after CORS and before authentication: an error answered without CORS headers reaches a browser
    /// as an opaque network failure, and failures in authentication itself still have to come back as a
    /// problem. Needs <see cref="UsePlatformTracing"/> earlier in the pipeline for the identifier.
    /// </remarks>
    public static WebApplication UsePlatformErrorHandling(this WebApplication app)
    {
        app.UseExceptionHandler();

        // With a problem writer registered, the default handler writes a problem for a 4xx or 5xx response
        // that has no body yet, and leaves responses that already have one alone.
        app.UseStatusCodePages();

        return app;
    }

    /// <summary>
    /// Model validation reports fields by their C# names ("Name", "Items[0].Price"); the response body
    /// names them by the JSON naming policy. Each segment of the path is renamed so the two match.
    /// </summary>
    private static void RenameFields(IDictionary<string, string[]> errors, JsonNamingPolicy? naming)
    {
        if (naming is null || errors.Count == 0)
        {
            return;
        }

        var renamed = errors.ToDictionary(
            pair => string.Join('.', pair.Key.Split('.').Select(segment => Rename(segment, naming))),
            pair => pair.Value);

        errors.Clear();
        foreach (var (field, messages) in renamed)
        {
            errors[field] = errors.TryGetValue(field, out var existing) ? [..existing, ..messages] : messages;
        }
    }

    private static string Rename(string segment, JsonNamingPolicy naming)
    {
        // "Items[0]": the index stays as it is, only the name in front of it is renamed.
        var bracket = segment.IndexOf('[');
        var name = bracket < 0 ? segment : segment[..bracket];
        var suffix = bracket < 0 ? string.Empty : segment[bracket..];

        return name.Length == 0 || name == "$" ? segment : naming.ConvertName(name) + suffix;
    }
}
