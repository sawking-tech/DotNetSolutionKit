namespace NamespaceRoot.ProductName.Common.Domain.Policy;

/// <summary>
/// Access rules over a tenant tree stored as materialized paths (<see cref="IHierarchicalEntity"/>).
/// </summary>
/// <remarks>
/// Every service-side policy that decides "may this tenant see that one" goes through these checks, so
/// the tree is interpreted the same way everywhere. Comparing path prefixes needs no query: both paths
/// are already on the entities.
/// </remarks>
public static class HierarchyRules
{
    /// <summary>
    /// The level that platform users manage directly: the top-level tenants.
    /// </summary>
    private const int PlatformManagedLevel = 1;

    /// <summary>
    /// True when a platform user may manage <paramref name="target"/>: an entity outside the tree
    /// (<c>null</c>) or a top-level tenant. Tenants below the top level are managed by their parents.
    /// </summary>
    public static bool IsInPlatformScope(this IHierarchicalEntity? target)
    {
        if (target == null) return true;

        return target.Level == PlatformManagedLevel;
    }

    /// <summary>
    /// True when <paramref name="target"/> is <paramref name="actor"/> itself or anywhere below it in
    /// the same branch.
    /// </summary>
    public static bool IsInTenantScope(this IHierarchicalEntity actor, IHierarchicalEntity target)
    {
        // A target higher in the tree than the actor cannot be below it.
        if (target.Path.Length < actor.Path.Length) return false;

        return target.Path.AsSpan(0, actor.Path.Length)
            .SequenceEqual(actor.Path.AsSpan());
    }

    /// <summary>
    /// True when <paramref name="other"/> is a direct child of <paramref name="actor"/>: one level deeper,
    /// with the actor's path as its prefix.
    /// </summary>
    public static bool IsDirectChild(this IHierarchicalEntity actor, IHierarchicalEntity other)
        => other.Path.Length == actor.Path.Length + 1 &&
           other.Path.AsSpan(0, actor.Path.Length).SequenceEqual(actor.Path.AsSpan());

    /// <summary>
    /// True when <paramref name="other"/> is the direct parent of <paramref name="actor"/>: one level
    /// shallower, and its path is the prefix of the actor's path.
    /// </summary>
    public static bool IsDirectParent(this IHierarchicalEntity actor, IHierarchicalEntity other)
        => other.Path.Length == actor.Path.Length - 1 &&
           actor.Path.AsSpan(0, other.Path.Length).SequenceEqual(other.Path.AsSpan());

    /// <summary>
    /// True when <paramref name="target"/> is the actor itself or its direct child.
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="IsInTenantScope"/>, which spans the whole subtree. Use it where a tenant
    /// may see its children's data but not its grandchildren's.
    /// </remarks>
    public static bool IsInDirectScope(this IHierarchicalEntity actor, IHierarchicalEntity target)
        => actor.Path.AsSpan().SequenceEqual(target.Path.AsSpan())
           || actor.IsDirectChild(target);
}
