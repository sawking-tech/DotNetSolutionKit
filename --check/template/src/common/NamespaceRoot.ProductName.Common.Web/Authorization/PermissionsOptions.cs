using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Common.Web.Authorization;

/// <summary>
/// Where a service takes the user's permissions from: the <c>Permissions</c> section.
/// </summary>
public sealed class PermissionsOptions
{
    public const string SectionName = "Permissions";

    /// <summary>
    /// <c>claims</c> (default): the permissions claims of the token, or those the gateway forwarded.
    /// <c>remote</c>: asked from a separate service, see <see cref="Remote"/>.
    /// </summary>
    [RegularExpression("^(claims|remote)$", ErrorMessage = "Permissions:Source is claims or remote")]
    public string Source { get; set; } = "claims";

    public RemotePermissionsOptions Remote { get; set; } = new();

    public bool IsRemote => Source == "remote";
}

/// <summary>
/// The service that answers a user's permissions, for <c>Permissions:Source</c> = <c>remote</c>.
/// </summary>
public sealed class RemotePermissionsOptions
{
    /// <summary>Where the permission service runs, such as <c>http://permissions:8080/</c>.</summary>
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>
    /// The path that answers a user's permissions as a JSON array of strings; <c>{userId}</c> is replaced
    /// with the user's id.
    /// </summary>
    public string Path { get; set; } = "/api/v1/permissions/users/{userId}";

    /// <summary>
    /// How long a user's answer is reused. A short time: a permission taken away stops working within it.
    /// </summary>
    [Range(0, 3600)]
    public int CacheSeconds { get; set; } = 30;
}
