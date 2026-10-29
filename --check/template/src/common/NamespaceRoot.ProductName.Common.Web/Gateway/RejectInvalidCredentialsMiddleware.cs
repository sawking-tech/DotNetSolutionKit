using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NamespaceRoot.ProductName.Common.Web.Authentication;

namespace NamespaceRoot.ProductName.Common.Web.Gateway;

/// <summary>
/// Answers 401 at the gateway when a caller presented a token that failed validation.
/// </summary>
/// <remarks>
/// A request without a token goes on anonymously and the service decides whether its endpoint is
/// public. A request whose token was rejected must not: forwarded without the user, it would reach a
/// public endpoint as an anonymous call, and a protected one would answer with an error about a missing
/// token that the caller did send.
/// </remarks>
public static class RejectInvalidCredentialsMiddleware
{
    public static IApplicationBuilder UseRejectInvalidCredentials(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var presented =
                context.Request.Headers.Authorization.FirstOrDefault()?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                || !string.IsNullOrEmpty(context.Request.Cookies[ServiceAuthenticationSetup.AccessTokenCookieName]);

            if (presented && context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
}
