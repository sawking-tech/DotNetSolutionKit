namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Business logic layer exceptions with support for specific error codes.
/// </summary>
public class BusinessLogicException : Exception
{
    public string? ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance with a message and an optional error code.
    /// </summary>
    public BusinessLogicException(string? message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance with a message, inner exception, and an optional error code.
    /// </summary>
    public BusinessLogicException(string? message, Exception innerException, string? errorCode = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
