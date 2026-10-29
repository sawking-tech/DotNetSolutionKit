namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Thrown when a downstream service is temporarily unavailable.
/// </summary>
public class ServiceUnavailableException : Exception
{
    public string? ErrorCode { get; }

    public ServiceUnavailableException(string? message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public ServiceUnavailableException(string? message, Exception innerException, string? errorCode = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
