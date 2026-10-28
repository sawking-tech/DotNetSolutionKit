// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Authorization;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Web.Authorization;

/// <summary>
/// Refuses an action marked <see cref="RequiredPermissionsAttribute"/> unless the current user holds
/// every permission it names.
/// </summary>
/// <remarks>
/// Runs for every action, so <see cref="IPermissionService"/> is resolved only for an action that
/// requires permissions. An anonymous caller is challenged (401) rather than refused (403): it has not
/// said who it is yet.
/// </remarks>
public sealed class PermissionAuthorizationFilter : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var requiredPermissions = context.ActionDescriptor.EndpointMetadata
            .OfType<RequiredPermissionsAttribute>()
            .SelectMany(attribute => attribute.Permissions)
            .Distinct()
            .ToList();

        if (requiredPermissions.Count == 0) return;

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var permissionService = context.HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        var hasAccess = await permissionService.UserHasAllPermissionsAsync(
            requiredPermissions, context.HttpContext.RequestAborted);

        if (!hasAccess)
        {
            throw new AccessDeniedException(
                $"Missing permissions: {string.Join(", ", requiredPermissions)}", "INSUFFICIENT_PERMISSIONS");
        }
    }
}
