namespace NamespaceRoot.ProductName.Common.Attributes;

/// <summary>
/// On a property: excludes it from the recorded diff of an <see cref="AuditableAttribute"/> entity.
/// Use for values that change on every write without operator meaning — cached counters,
/// background-processing state, synchronisation markers.
/// </summary>
/// <remarks>
/// <para>
/// On an aggregate: states that the aggregate is deliberately not audited. It records the decision
/// so a reader can tell "considered and rejected" from "nobody looked", and it is what the marker
/// guard test accepts in place of <see cref="AuditableAttribute"/>. Give the reason in a comment —
/// the usual ones are a projection of another service's data, a dedicated channel that already
/// records the change, and infrastructure tables.
/// </para>
/// <para>
/// Infrastructure columns shared by all entities (<c>CreatedAt</c>, <c>UpdatedAt</c>, row version)
/// are filtered globally and need no attribute.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, Inherited = false)]
public sealed class AuditIgnoreAttribute : Attribute;
