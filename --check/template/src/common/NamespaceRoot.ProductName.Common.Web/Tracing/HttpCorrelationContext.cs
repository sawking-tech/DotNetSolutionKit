using Microsoft.AspNetCore.Http;
using NamespaceRoot.ProductName.Common.Application.Tracing;

namespace NamespaceRoot.ProductName.Common.Web.Tracing;

/// <summary>
/// Reads the correlation identifier the middleware put on the request.
/// </summary>
/// <remarks>
/// Outside a request — a background job, a bus consumer — there is no HTTP context, and the identifier
/// comes from the message or is created for the job. Returning an empty string here instead would put
/// unfindable lines in the log, so a fresh identifier is produced and the work is still traceable.
/// </remarks>
public sealed class HttpCorrelationContext : ICorrelationContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCorrelationContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string CorrelationId =>
        _httpContextAccessor.HttpContext?.Items[TracingProperties.CorrelationId] as string
        ?? Correlation.Current
        ?? System.Diagnostics.Activity.Current?.TraceId.ToString()
        ?? Guid.NewGuid().ToString("n");
}
