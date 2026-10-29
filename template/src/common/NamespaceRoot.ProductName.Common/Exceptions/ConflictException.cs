namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Occurs when a conflict situation arises
/// </summary>
public sealed class ConflictException : BusinessLogicException
{
    /// <inheritdoc />
    public ConflictException(string? message, string? errorCode = null) : base(message, errorCode) { }

    /// <inheritdoc />
    public ConflictException(string? message, Exception innerException, string? errorCode = null) : base(message, innerException, errorCode) { }
}
