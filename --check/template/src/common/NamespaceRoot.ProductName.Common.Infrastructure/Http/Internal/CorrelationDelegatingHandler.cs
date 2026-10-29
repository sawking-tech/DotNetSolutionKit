using NamespaceRoot.ProductName.Common.Application.Tracing;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Http.Internal;

/// <summary>
/// Puts the correlation identifier of the running work on an outgoing call, so the called service logs
/// under the same identifier.
/// </summary>
/// <remarks>
/// The trace crosses on its own: the client factory writes <c>traceparent</c> from the current
/// <c>Activity</c>. The identifier does not, and a caller that chose its own would lose it at the first
/// call to another service.
/// </remarks>
public sealed class CorrelationDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Correlation.Current is { } correlationId && !request.Headers.Contains(TracingHeaders.CorrelationId))
            request.Headers.TryAddWithoutValidation(TracingHeaders.CorrelationId, correlationId);

        return base.SendAsync(request, cancellationToken);
    }
}
