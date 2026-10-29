using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Http.Internal;

/// <summary>
/// Passes the caller on to the service being called: the person a request acts for, with the tenant,
/// roles and permissions it came with, or the system when there is no person.
/// </summary>
/// <remarks>
/// <para>
/// The person is read from <see cref="HttpContext.User"/> through the accessor rather than from an
/// injected <see cref="IUserContext"/>: the client factory builds its handlers in a scope of its own, and a
/// scoped context resolved there is not the request's. The claims are also the only place the roles and
/// permissions are: <see cref="IUserContext"/> does not carry them, and the called service checks its
/// permissions against what arrives here, the same way it checks what the gateway sends.
/// </para>
/// <para>
/// Work with no request behind it - a job, a consumer - calls as the system, which the receiving service
/// accepts only next to the internal key (<see cref="InternalApiKeyDelegatingHandler"/>). An anonymous
/// request passes nobody on: the called service sees the key without a user and refuses it wherever a
/// user is required.
/// </para>
/// </remarks>
public sealed class UserContextDelegatingHandler(
    IHttpContextAccessor httpContextAccessor,
    ILogger<UserContextDelegatingHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (user is null || IsSystem(user))
        {
            Set(request, AuthHeaders.SystemCall, "true");
            logger.LogTrace("Calling {Uri} as the system", request.RequestUri);
            return base.SendAsync(request, cancellationToken);
        }

        // An anonymous request names nobody. Calling as the system here would hand an open endpoint the
        // system's rights in the next service.
        if (user.Identity?.IsAuthenticated != true)
            return base.SendAsync(request, cancellationToken);

        var userId = user.FindFirstValue(AuthClaims.UserId);
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException("The authenticated caller has no user_id claim to pass on");

        Set(request, AuthHeaders.UserId, userId);
        Set(request, AuthHeaders.AuthType, user.FindFirstValue(AuthClaims.AuthType));
        Set(request, AuthHeaders.AuthId,
            user.FindFirstValue(AuthClaims.Jti)
            ?? user.FindFirstValue(AuthClaims.ApiKeyId)
            ?? user.FindFirstValue(AuthClaims.AuthId));
        Set(request, AuthHeaders.AuthExp, user.FindFirstValue(AuthClaims.Exp) ?? user.FindFirstValue(AuthClaims.AuthExp));
        Set(request, AuthHeaders.AuthValidated, user.FindFirstValue(AuthClaims.AuthValidated));
        Set(request, AuthHeaders.UserLogin, user.FindFirstValue(AuthClaims.UserLogin));
        Set(request, AuthHeaders.UserDisplayName, user.FindFirstValue(AuthClaims.DisplayName));
        Set(request, AuthHeaders.TenantId, user.FindFirstValue(AuthClaims.TenantId));
        Set(request, AuthHeaders.UserRoles, Join(user, AuthClaims.UserRole));
        Set(request, AuthHeaders.UserPermissions, Join(user, AuthClaims.Permissions));

        logger.LogTrace("Calling {Uri} for user {UserId}", request.RequestUri, userId);
        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsSystem(ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue(AuthClaims.AuthType), AuthMethod.System.ToString(), StringComparison.OrdinalIgnoreCase);

    private static string Join(ClaimsPrincipal user, string claimType) =>
        string.Join(',', user.FindAll(claimType).Select(c => c.Value));

    // A claim is client data that ends up in a header: a line break in it would start a header of its own.
    private static void Set(HttpRequestMessage request, string name, string? value)
    {
        var clean = value?.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (string.IsNullOrEmpty(clean)) return;

        request.Headers.Remove(name);
        request.Headers.TryAddWithoutValidation(name, clean);
    }
}
