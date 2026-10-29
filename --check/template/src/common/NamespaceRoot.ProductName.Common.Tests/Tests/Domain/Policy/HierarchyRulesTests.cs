using NamespaceRoot.ProductName.Common.Domain;
using NamespaceRoot.ProductName.Common.Domain.Policy;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Domain.Policy;

/// <summary>
/// Branch coverage for the tree rules: every service-side access policy over the tenant tree relies on
/// them, so a regression here breaks all of those policies at once.
/// </summary>
[TestFixture]
[TestOf(typeof(HierarchyRules))]
[Parallelizable(ParallelScope.All)]
internal class HierarchyRulesTests
{
    private sealed record Node(int[] Path) : IHierarchicalEntity
    {
        public int Level => Path.Length;
    }

    // ── IsInPlatformScope ─────────────────────────────────────────────────────

    [Test(Description = "Platform target (null) is always in platform scope")]
    public void Should_BeInPlatformScope_When_TargetIsNull()
    {
        ((IHierarchicalEntity?)null).IsInPlatformScope().ShouldBeTrue();
    }

    [Test(Description = "A top-level tenant is in platform scope")]
    public void Should_BeInPlatformScope_When_TargetIsLevel1()
    {
        new Node([5001]).IsInPlatformScope().ShouldBeTrue();
    }

    [Test(Description = "A level-2 tenant is NOT in platform scope")]
    public void Should_NotBeInPlatformScope_When_TargetIsLevel2()
    {
        new Node([5001, 5002]).IsInPlatformScope().ShouldBeFalse();
    }

    [Test(Description = "A level-3 tenant is NOT in platform scope")]
    public void Should_NotBeInPlatformScope_When_TargetIsLevel3()
    {
        new Node([5001, 5002, 5003]).IsInPlatformScope().ShouldBeFalse();
    }

    // ── IsInTenantScope (prefix match) ────────────────────────────────────────

    [Test(Description = "Self is in its own tenant scope (path == path)")]
    public void Should_BeInTenantScope_When_TargetIsSelf()
    {
        var actor = new Node([5001, 5002]);
        actor.IsInTenantScope(actor).ShouldBeTrue();
    }

    [Test(Description = "Direct descendant is in scope")]
    public void Should_BeInTenantScope_When_TargetIsDescendant()
    {
        var actor = new Node([5001]);
        var child = new Node([5001, 5002]);
        actor.IsInTenantScope(child).ShouldBeTrue();
    }

    [Test(Description = "Deep descendant is in scope (path is prefixed)")]
    public void Should_BeInTenantScope_When_TargetIsDeepDescendant()
    {
        var actor = new Node([5001]);
        var grandchild = new Node([5001, 5002, 5003]);
        actor.IsInTenantScope(grandchild).ShouldBeTrue();
    }

    [Test(Description = "Ancestor is NOT in scope (target path is shorter)")]
    public void Should_NotBeInTenantScope_When_TargetIsAncestor()
    {
        var actor = new Node([5001, 5002, 5003]);
        var ancestor = new Node([5001]);
        actor.IsInTenantScope(ancestor).ShouldBeFalse();
    }

    [Test(Description = "Sibling (same level, different path) is NOT in scope")]
    public void Should_NotBeInTenantScope_When_TargetIsSibling()
    {
        var actor = new Node([5001, 5002]);
        var sibling = new Node([5001, 5003]);
        actor.IsInTenantScope(sibling).ShouldBeFalse();
    }

    [Test(Description = "Cross-branch (different root) is NOT in scope even if levels match")]
    public void Should_NotBeInTenantScope_When_TargetIsInOtherBranch()
    {
        var actor = new Node([5001, 5002]);
        var other = new Node([9999, 8888]);
        actor.IsInTenantScope(other).ShouldBeFalse();
    }

    // ── IsDirectChild ─────────────────────────────────────────────────────────

    [Test(Description = "Direct child: one level deeper, prefix matches")]
    public void Should_BeDirectChild_When_PathIsExactlyOneDeeper()
    {
        var actor = new Node([5001]);
        var child = new Node([5001, 5002]);
        actor.IsDirectChild(child).ShouldBeTrue();
    }

    [Test(Description = "Grandchild is NOT a direct child (two levels deeper)")]
    public void Should_NotBeDirectChild_When_PathIsTwoLevelsDeeper()
    {
        var actor = new Node([5001]);
        var grandchild = new Node([5001, 5002, 5003]);
        actor.IsDirectChild(grandchild).ShouldBeFalse();
    }

    [Test(Description = "Same-level node is NOT a direct child")]
    public void Should_NotBeDirectChild_When_PathIsSameLevel()
    {
        var actor = new Node([5001, 5002]);
        var sibling = new Node([5001, 5003]);
        actor.IsDirectChild(sibling).ShouldBeFalse();
    }

    [Test(Description = "Cross-branch node is NOT a direct child even when one level deeper")]
    public void Should_NotBeDirectChild_When_OneLevelDeeperButDifferentPrefix()
    {
        var actor = new Node([5001]);
        var other = new Node([9999, 8888]);
        actor.IsDirectChild(other).ShouldBeFalse();
    }

    // ── IsDirectParent ────────────────────────────────────────────────────────

    [Test(Description = "Direct parent: one level shallower, prefix matches actor")]
    public void Should_BeDirectParent_When_PathIsExactlyOneShallower()
    {
        var actor = new Node([5001, 5002]);
        var parent = new Node([5001]);
        actor.IsDirectParent(parent).ShouldBeTrue();
    }

    [Test(Description = "Two-levels-up ancestor is NOT a direct parent")]
    public void Should_NotBeDirectParent_When_PathIsTwoLevelsShallower()
    {
        var actor = new Node([5001, 5002, 5003]);
        var grandparent = new Node([5001]);
        actor.IsDirectParent(grandparent).ShouldBeFalse();
    }

    [Test(Description = "Same-level node is NOT a direct parent")]
    public void Should_NotBeDirectParent_When_PathIsSameLevel()
    {
        var actor = new Node([5001, 5002]);
        var sibling = new Node([5001, 5003]);
        actor.IsDirectParent(sibling).ShouldBeFalse();
    }

    [Test(Description = "Cross-branch shallower node is NOT a direct parent")]
    public void Should_NotBeDirectParent_When_OneLevelShallowerButDifferentPrefix()
    {
        var actor = new Node([5001, 5002]);
        var other = new Node([9999]);
        actor.IsDirectParent(other).ShouldBeFalse();
    }

    // ── IsInDirectScope (self and direct children only) ─────────────────────

    [Test(Description = "Direct scope includes the actor itself")]
    public void Should_BeInDirectScope_When_TargetIsSelf()
    {
        var actor = new Node([5001, 5002]);
        actor.IsInDirectScope(new Node([5001, 5002])).ShouldBeTrue();
    }

    [Test(Description = "Direct scope includes a direct child")]
    public void Should_BeInDirectScope_When_TargetIsDirectChild()
    {
        var actor = new Node([5001]);
        actor.IsInDirectScope(new Node([5001, 5002])).ShouldBeTrue();
    }

    [Test(Description = "Direct scope EXCLUDES a grandchild (skip-level)")]
    public void Should_NotBeInDirectScope_When_TargetIsGrandchild()
    {
        var actor = new Node([5001]);
        actor.IsInDirectScope(new Node([5001, 5002, 5003])).ShouldBeFalse();
    }

    [Test(Description = "Direct scope excludes a sibling")]
    public void Should_NotBeInDirectScope_When_TargetIsSibling()
    {
        var actor = new Node([5001, 5002]);
        actor.IsInDirectScope(new Node([5001, 5003])).ShouldBeFalse();
    }

    [Test(Description = "Direct scope excludes an ancestor")]
    public void Should_NotBeInDirectScope_When_TargetIsAncestor()
    {
        var actor = new Node([5001, 5002]);
        actor.IsInDirectScope(new Node([5001])).ShouldBeFalse();
    }
}
