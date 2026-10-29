namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Thrown when a concurrency conflict occurs, e.g., when multiple processes attempt to modify the same entity.
/// </summary>
public class ConcurrencyException : BusinessLogicException
{
    /// <summary>
    /// Initializes a new instance of <see cref="ConcurrencyException"/> with a specified error message and optional error code.
    /// </summary>
    /// <param name="message">The error message describing the concurrency conflict.</param>
    /// <param name="errorCode">Optional machine-readable error code.</param>
    public ConcurrencyException(string? message, string? errorCode = null)
        : base(message, errorCode)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ConcurrencyException"/> with a specified error message, inner exception, and optional error code.
    /// </summary>
    /// <param name="message">The error message describing the concurrency conflict.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    /// <param name="errorCode">Optional machine-readable error code.</param>
    public ConcurrencyException(string? message, Exception innerException, string? errorCode = null)
        : base(message, innerException, errorCode)
    {
    }
}
