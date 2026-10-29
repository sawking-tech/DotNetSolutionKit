namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Occurs when the current user doesn't have access to the resource.
/// </summary>
public sealed class AccessDeniedException : BusinessLogicException
{
    /// <summary>
    /// Initializes a new instance of <see cref="AccessDeniedException"/>.
    /// </summary>
    /// <param name="message">The error message describing the reason for the exception.</param>
    /// <param name="errorCode">Specific security error code (e.g., "INSUFFICIENT_PERMISSIONS").</param>
    public AccessDeniedException(string? message = null, string? errorCode = null)
        : base(message, errorCode)
    {
    }
}
