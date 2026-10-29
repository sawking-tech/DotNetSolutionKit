namespace NamespaceRoot.ProductName.Common.Domain.Idempotency;

/// <summary>
/// The store of what has already been carried out under a given key.
/// </summary>
/// <remarks>
/// Narrower than a repository on purpose: the log is addressed only by the pair its unique index is built
/// on, and nothing else is a question the platform's own code asks it.
/// </remarks>
public interface IIdempotencyLog
{
    /// <summary>What was recorded for this key within this scope, or <c>null</c> when nothing was.</summary>
    Task<IdempotencyRecord?> FindAsync(string scope, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks a new record. It reaches storage with the unit of work's save, the same save as the work it
    /// describes.
    /// </summary>
    void Add(IdempotencyRecord record);
}
