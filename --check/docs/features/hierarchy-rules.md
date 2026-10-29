# Access rules over a tenant tree

Generated with `--HierarchyRules`, off by default. Pass it with `--Solution`.

For a product where tenants form a tree (a tenant has child tenants, and they have theirs), the template
adds the rules that answer whether one tenant may see another, without a query.

## The tree

An entity in the tree implements `IHierarchicalEntity`: its materialized path, the identifiers from the
root down to the entity itself, and its level.

```csharp
public class Tenant : AggregateRoot<Guid>, IHierarchicalEntity
{
    public int[] Path { get; private set; } = [];   // [12, 40, 41]: root 12, then 40, then this one
    public int Level => Path.Length;
}
```

## The rules

`HierarchyRules` compares paths:

| Rule | True when the target is |
|---|---|
| `target.IsInPlatformScope()` | outside the tree (`null`) or a top-level tenant: what a platform user manages directly |
| `actor.IsInTenantScope(target)` | the actor itself or anywhere below it in its branch |
| `actor.IsDirectChild(target)` | one level below the actor, in its branch |
| `actor.IsDirectParent(target)` | one level above the actor, on its path |
| `actor.IsInDirectScope(target)` | the actor itself or its direct child, for data a tenant may see for its children but not its grandchildren |

Every service-side policy that decides whether a tenant may see another goes through these rules, so the
tree means the same thing in every service.

## Tests

`Common.Tests` covers each rule: self, child, grandchild, sibling, ancestor, another branch, and the levels
of the platform scope.
