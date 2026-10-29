namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Thrown when a unique constraint violation occurs, e.g., duplicate entity.
/// </summary>
public sealed class UniqueViolationException : BusinessLogicException
{
    /// <summary>
    /// Initializes a new instance of <see cref="UniqueViolationException"/> with a specified error
    /// message and an optional error code identifying which constraint was hit.
    /// </summary>
    /// <param name="message">The error message describing the unique constraint violation.</param>
    /// <param name="errorCode">Machine-readable code so the caller can point at the offending field.</param>
    public UniqueViolationException(string? message = null, string? errorCode = null) : base(message, errorCode) { }

    /// <summary>
    /// Initializes a new instance of <see cref="UniqueViolationException"/> with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The error message describing the unique constraint violation.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    /// <param name="errorCode">Machine-readable code so the caller can point at the offending field.</param>
    public UniqueViolationException(string? message, Exception innerException, string? errorCode = null)
        : base(message, innerException, errorCode) { }
}
