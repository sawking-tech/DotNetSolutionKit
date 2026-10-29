namespace NamespaceRoot.ProductName.Common.Domain.Idempotency;

/// <summary>
/// A command a client may send more than once and expect carried out once: it carries a key the client
/// chose, the same on every retry.
/// </summary>
/// <remarks>
/// A client retries because a connection dropped or a queue redelivered, and it cannot tell whether the
/// first attempt went through. Implementing this interface states that a repeat is harmless; the key is
/// what <c>IIdempotentExecutor</c> uses to make it so.
/// </remarks>
public interface IIdempotentRequest
{
    string IdempotencyKey { get; init; }
}

/// <summary>
/// The bounds an idempotency key has to fall within.
/// </summary>
/// <remarks>
/// The lower bound is the point of the key: a short one collides between clients that both picked
/// "retry-1". The upper bound is what the log's unique index carries.
/// </remarks>
public static class IdempotencyKeyLimits
{
    public const int MinLength = 16;

    public const int MaxLength = 128;
}
