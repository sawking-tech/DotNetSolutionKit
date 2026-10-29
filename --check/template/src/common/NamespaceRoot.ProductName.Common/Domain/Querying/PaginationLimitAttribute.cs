namespace NamespaceRoot.ProductName.Common.Domain.Querying;

/// <summary>
/// Caps how large a page of this request may be.
/// </summary>
/// <remarks>
/// Declared on the request type rather than checked at the call site: the limit belongs to the shape being
/// asked for, and a caller reading a listing cannot know what the endpoint can afford to return. A request
/// without it gets the shared default of the web layer.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PaginationLimitAttribute(int maximumPageSize) : Attribute
{
    /// <summary>Largest page this request will be served.</summary>
    public int MaximumPageSize { get; } = maximumPageSize;
}
