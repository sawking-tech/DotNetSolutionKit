namespace NamespaceRoot.ProductName.Common.Web.Authorization;

/// <summary>
/// Specifies that access to a controller or action requires the specified permissions
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequiredPermissionsAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredPermissionsAttribute"/> class
    /// </summary>
    /// <param name="permissions">One or more permission codes required to access the resource</param>
    public RequiredPermissionsAttribute(params string[] permissions)
    {
        Permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));

        if (Permissions.Count == 0)
            throw new ArgumentException("At least one permission must be specified", nameof(permissions));
    }

    /// <summary>
    /// Gets the permission codes required to access the resource
    /// </summary>
    public IReadOnlyList<string> Permissions { get; }
}
