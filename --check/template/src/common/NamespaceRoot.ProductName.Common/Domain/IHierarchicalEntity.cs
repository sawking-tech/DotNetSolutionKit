namespace NamespaceRoot.ProductName.Common.Domain;

/// <summary>
/// An entity placed in a tree by its materialized path: the identifiers of its ancestors, root first,
/// ending with its own.
/// </summary>
public interface IHierarchicalEntity
{
    /// <summary>
    /// Identifiers from the root down to this entity, inclusive.
    /// </summary>
    int[] Path { get; }

    /// <summary>
    /// Depth in the tree; a top-level entity is at level 1.
    /// </summary>
    int Level { get; }
}
