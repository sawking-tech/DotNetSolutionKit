namespace NamespaceRoot.ProductName.Common.Attributes;

/// <summary>
/// Marks a property whose CHANGE must be recorded but whose VALUES must not — password hashes,
/// key material, salts.
/// </summary>
/// <remarks>
/// The difference from <see cref="AuditIgnoreAttribute"/> is the whole point of this attribute.
/// Ignoring a secret would erase the event as well as the value: a password change touches nothing
/// else, so the diff would come back empty and the interceptor drops empty diffs. "The password was
/// changed, by whom and when" is exactly what an audit journal exists to answer, so the property
/// stays in the diff and both sides are replaced with <see cref="Placeholder"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class AuditRedactAttribute : Attribute
{
    /// <summary>Stands in for a redacted value on both sides of the diff.</summary>
    public const string Placeholder = "***";
}
