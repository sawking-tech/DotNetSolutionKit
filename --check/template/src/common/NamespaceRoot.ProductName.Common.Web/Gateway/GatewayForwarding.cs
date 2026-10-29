using System.Net.Http.Headers;
using System.Security.Claims;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Web.Gateway;

/// <summary>
/// What a gateway passes to a service about the caller.
/// </summary>
/// <remarks>
/// The gateway validates the token; a service behind it trusts the user context headers because they
/// come with the internal API key, which only the gateway and the services hold. So the gateway first
/// removes the key and every context header the client sent - a client that set <c>X-User-Id</c> itself
/// would otherwise act as anyone - and then, for an authenticated caller only, sets its own.
/// </remarks>
public static class GatewayForwarding
{
    public static void ForwardUser(HttpRequestHeaders outgoing, ClaimsPrincipal user, string internalApiKey)
    {
        outgoing.Remove(AuthHeaders.ApiKey);
        foreach (var header in AuthHeaders.UserContext)
            outgoing.Remove(header);

        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(internalApiKey))
            return;

        outgoing.TryAddWithoutValidation(AuthHeaders.ApiKey, internalApiKey);
        Set(outgoing, AuthHeaders.AuthType, AuthMethod.Jwt.ToString());
        Set(outgoing, AuthHeaders.UserId, user.FindFirstValue(AuthClaims.UserId));
        Set(outgoing, AuthHeaders.AuthId, user.FindFirstValue(AuthClaims.Jti));
        Set(outgoing, AuthHeaders.AuthExp, user.FindFirstValue(AuthClaims.Exp));
        Set(outgoing, AuthHeaders.UserLogin, user.FindFirstValue(AuthClaims.UserLogin));
        Set(outgoing, AuthHeaders.UserDisplayName, user.FindFirstValue(AuthClaims.DisplayName));
        Set(outgoing, AuthHeaders.TenantId, user.FindFirstValue(AuthClaims.TenantId));
        Set(outgoing, AuthHeaders.UserRoles, Join(user, AuthClaims.UserRole));
        Set(outgoing, AuthHeaders.UserPermissions, Join(user, AuthClaims.Permissions));
    }

    private static string Join(ClaimsPrincipal user, string claimType) =>
        string.Join(',', user.FindAll(claimType).Select(c => c.Value));

    // A claim is client data that ends up in a header: a line break in it would start a header of its own.
    private static void Set(HttpRequestHeaders headers, string name, string? value)
    {
        var clean = value?.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        if (!string.IsNullOrEmpty(clean))
            headers.TryAddWithoutValidation(name, clean);
    }
}
