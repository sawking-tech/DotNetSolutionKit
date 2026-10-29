namespace NamespaceRoot.ProductName.Common.Application.Tracing;

/// <summary>
/// Header names carrying the trace across a service boundary.
/// </summary>
public static class TracingHeaders
{
    /// <summary>
    /// The correlation identifier. Accepted from the caller and echoed back on the response, so a client
    /// reporting a problem can name the identifier and it can be found in the logs.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";
}

/// <summary>
/// Log property names, written once so every service spells them the same way and a query across services
/// does not need to know which service wrote the line.
/// </summary>
/// <remarks>
/// Only the correlation id is written by the platform itself. <c>TraceId</c>, <c>SpanId</c> and
/// <c>ParentId</c> come from the Serilog span enricher, which reads the same <c>Activity</c> and also
/// covers work that happens outside a request.
/// </remarks>
public static class TracingProperties
{
    /// <summary>Ties one external request to everything it caused.</summary>
    public const string CorrelationId = "CorrelationId";
}
