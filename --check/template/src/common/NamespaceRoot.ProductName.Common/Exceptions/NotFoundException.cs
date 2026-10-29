namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Occurs when entity is not found in the system
/// </summary>
public class NotFoundException(string? message) : Exception(message)
{
    public NotFoundException(Type entityType, object id) : this($"Not Found {entityType} by id {id}")
    {
    }
}
