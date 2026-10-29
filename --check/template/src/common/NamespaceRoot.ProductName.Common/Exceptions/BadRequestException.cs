namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Thrown when a request parameter is syntactically valid but semantically rejected —
/// e.g. a <c>sortBy</c> value that is not in the allowed whitelist.
/// Maps to HTTP 400 Bad Request.
/// </summary>
public class BadRequestException : Exception
{
    public string? ErrorCode { get; }

    /// <summary>Initializes a new instance with a message and an optional error code.</summary>
    public BadRequestException(string? message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
