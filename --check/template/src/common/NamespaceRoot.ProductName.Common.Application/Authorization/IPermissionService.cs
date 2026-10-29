namespace NamespaceRoot.ProductName.Common.Application.Authorization;

/// <summary>
/// Answers whether the current user holds the permissions an action requires.
/// </summary>
/// <remarks>
/// The default implementation reads the permissions from the user's token. A service whose permissions
/// live elsewhere, such as in a separate authorization service, registers its own implementation.
/// </remarks>
public interface IPermissionService
{
    /// <summary>
    /// Returns <c>true</c> if the current user has ALL of the specified permissions.
    /// </summary>
    Task<bool> UserHasAllPermissionsAsync(
        IReadOnlyCollection<string> allRequiredPermissions,
        CancellationToken cancellationToken);
}
