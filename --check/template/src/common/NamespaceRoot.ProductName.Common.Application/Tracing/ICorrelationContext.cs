namespace NamespaceRoot.ProductName.Common.Application.Tracing;

/// <summary>
/// The identifier tying everything that happened because of one external request together — across
/// services, background jobs and bus messages.
/// </summary>
/// <remarks>
/// Business code reads the identifier from here rather than from <c>HttpContext</c>: the same code runs
/// inside a request, inside a job and inside a consumer, and only the first of those has an HTTP context.
/// </remarks>
public interface ICorrelationContext
{
    /// <summary>
    /// The correlation identifier for the work currently being done. Never empty.
    /// </summary>
    string CorrelationId { get; }
}
