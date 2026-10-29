namespace NamespaceRoot.ProductName.Common.Attributes;

/// <summary>
/// Marks an entity as auditable — changes to it are captured and forwarded to the audit journal.
/// Opt-in by design: an unmarked entity produces no audit records, so service tables (outbox,
/// projections) and high-volume rows stay out of the journal without an explicit exclusion list.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditableAttribute : Attribute
{
    /// <param name="module">
    /// Business module the entity belongs to (Orders, Catalog, Accounts, ...). Shown as a filter
    /// facet in the audit UI. Kept as a free-form string so a new module needs no migration.
    /// </param>
    public AuditableAttribute(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        Module = module;
    }

    /// <summary>Business module label recorded on every entry for this entity.</summary>
    public string Module { get; }

    /// <summary>
    /// Name of the property holding a human-readable identifier of the row: an order number, a
    /// customer name. Recorded alongside the technical id so the UI can render
    /// a meaningful row without resolving the entity.
    /// </summary>
    public string? DisplayProperty { get; init; }

    /// <summary>
    /// Name of the property holding the tenant the row belongs to. Recorded so that a change the system
    /// made, which has no acting tenant, can still be shown to the tenant it happened to.
    /// </summary>
    /// <remarks>
    /// The row's own tenant, never an ancestor in a tenant tree: who may read an entry is the journal's
    /// rule, and an entry has to name the tenant it is about, not everyone above it. Entities with no
    /// owner (reference data, platform-wide settings) leave this unset and stay platform-only.
    /// </remarks>
    public string? SubjectTenantProperty { get; init; }
}
