using Microsoft.AspNetCore.Http;
using NamespaceRoot.ProductName.Common.Application.Authorization;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Web.Authorization;

/// <summary>
/// Reads the current user's permissions from the <c>permissions</c> claims of the token.
/// </summary>
/// <remarks>
/// Needs nothing but the token, so it works for a solution with a single service. A JSON array in the
/// token becomes one claim per permission. A system call, made by the platform itself rather than by a
/// user, holds every permission.
/// </remarks>
public sealed class ClaimsPermissionService(
    IHttpContextAccessor httpContextAccessor,
    IUserContext userContext) : IPermissionService
{
    public Task<bool> UserHasAllPermissionsAsync(
        IReadOnlyCollection<string> allRequiredPermissions,
        CancellationToken cancellationToken)
    {
        if (allRequiredPermissions.Count == 0 || userContext.IsSystemCall)
            return Task.FromResult(true);

        var held = httpContextAccessor.HttpContext?.User
            .FindAll(AuthClaims.Permissions)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        return Task.FromResult(allRequiredPermissions.All(held.Contains));
    }
}
