namespace NamespaceRoot.ProductName.Common.Contracts.Responses;

/// <summary>
/// Represents a response containing a collection of items.
/// Used as a marker interface for responses that can be wrapped with a data envelope.
/// </summary>
/// <typeparam name="T">Type of the items in the collection.</typeparam>
public interface IItemsResponse<out T>
{
    /// <summary>
    /// Gets the collection of items.
    /// </summary>
    IReadOnlyList<T> Items { get; }
}
