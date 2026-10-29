namespace NamespaceRoot.ProductName.Common.Application.Tracing;

/// <summary>
/// The correlation identifier of the work running now, wherever it runs: a request, a consumer, a job.
/// </summary>
/// <remarks>
/// A request takes it from the caller's <c>X-Correlation-Id</c>, or from its trace. Whatever the work then
/// causes - a message, a job, a call to another service - carries it on, and the receiving side puts it back
/// here, so one search by the identifier finds every line the external request caused, in every service.
/// The trace alone does not do that: a caller that sent its own identifier would find it in the first
/// service's log and lose it at the first message.
/// </remarks>
public static class Correlation
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>The identifier of the running work, or <c>null</c> outside any.</summary>
    public static string? Current => Ambient.Value;

    /// <summary>Makes <paramref name="correlationId"/> current until the returned scope is disposed.</summary>
    public static IDisposable Use(string correlationId)
    {
        var previous = Ambient.Value;
        Ambient.Value = correlationId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
