namespace NamespaceRoot.ProductName.Common.Contracts.Schema;

/// <summary>
/// Declares where the accepted values of a string property come from, so the published schema can
/// list them instead of describing them in prose.
/// </summary>
/// <remarks>
/// A status field carries a closed set of values, but the set lives in code - a lookup table, a
/// constants class - while the schema only had the XML doc to say so. Prose goes stale silently, and a
/// client integrates against values that were renamed months ago. Pointing at the source instead means
/// the document is generated from the same thing the code checks against, so it cannot drift.
///
/// The member must be static and resolve to a sequence of strings: an <c>IEnumerable&lt;string&gt;</c>
/// directly, or an <c>IReadOnlyDictionary&lt;string, ...&gt;</c>, in which case its keys are used.
/// This attribute carries no dependency on the documentation stack - it only names a source, and
/// the filter that reads it lives with the other schema filters.
/// </remarks>
/// <param name="source">Type holding the member that lists the values.</param>
/// <param name="memberName">Name of the static field or property to read.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SchemaValuesFromAttribute(Type source, string memberName) : Attribute
{
    /// <summary>Type holding the member that lists the accepted values.</summary>
    public Type Source { get; } = source;

    /// <summary>Name of the static field or property to read.</summary>
    public string MemberName { get; } = memberName;
}
