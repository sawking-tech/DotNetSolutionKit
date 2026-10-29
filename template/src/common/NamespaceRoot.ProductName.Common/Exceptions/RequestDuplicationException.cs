namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Thrown when a request quotation is duplicated.
/// </summary>
public class RequestDuplicationException : BusinessLogicException
{
    /// <summary>
    /// Initializes a new instance of <see cref="RequestDuplicationException"/> with a specified message.
    /// </summary>
    /// <param name="message">The error message describing the duplication.</param>
    public RequestDuplicationException(string? message = null) : base(message) { }

    /// <summary>
    /// Initializes a new instance of <see cref="RequestDuplicationException"/> with a specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message describing the duplication.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public RequestDuplicationException(string? message, Exception innerException) : base(message, innerException) { }
}
