using JetBrains.Annotations;

namespace NamespaceRoot.ProductName.Common.Domain.Querying;

/// <summary>
/// Specifies the order in which items are returned in a result set.
/// </summary>
[PublicAPI]
public enum SortDirection
{
    /// <summary>
    /// Sorts from lowest to highest (e.g., A to Z, 1 to 10).
    /// </summary>
    Asc = 0,

    /// <summary>
    /// Sorts from highest to lowest (e.g., Z to A, 10 to 1).
    /// </summary>
    Desc = 1
}
