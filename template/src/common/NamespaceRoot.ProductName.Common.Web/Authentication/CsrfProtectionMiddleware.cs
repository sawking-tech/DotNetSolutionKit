// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using Microsoft.AspNetCore.Http;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Web.Authentication;

/// <summary>
/// Refuses a state-changing request that a token cookie would authenticate, unless it carries the
/// <see cref="HeaderName"/> header.
/// </summary>
/// <remarks>
/// <para>
/// A browser attaches the cookies to a request any page starts, so a cookie alone does not show that the
/// frontend sent it. A custom header does: a page on another origin cannot add one without a CORS
/// preflight, which only the allowed origins pass. With <c>SameSite=Lax</c> the browser already keeps the
/// cookie off a cross-site POST, and the header is a second layer; with <c>None</c> it is the protection.
/// </para>
/// <para>
/// A request carrying an <c>Authorization</c> header or an API key is not affected: no page can make a
/// browser add those on its own.
/// </para>
/// </remarks>
public sealed class CsrfProtectionMiddleware(RequestDelegate next, AuthCookieSettings settings)
{
    /// <summary>The header the frontend sends on every POST, PUT, PATCH and DELETE; any value.</summary>
    public const string HeaderName = "X-CSRF";

    public const string MissingHeaderCode = "CSRF_HEADER_MISSING";

    // Thrown rather than written: the error handling before it answers with the platform's problem, and
    // CORS before that adds its headers, so the frontend can read why.
    public Task InvokeAsync(HttpContext context)
    {
        if (settings.RequireCsrfHeader
            && NeedsHeader(context.Request)
            && string.IsNullOrEmpty(context.Request.Headers[HeaderName]))
            throw new AccessDeniedException(
                $"A request authenticated by a cookie has to carry the {HeaderName} header.",
                MissingHeaderCode);

        return next(context);
    }

    private static bool NeedsHeader(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method)
        && !HttpMethods.IsHead(request.Method)
        && !HttpMethods.IsOptions(request.Method)
        && !HttpMethods.IsTrace(request.Method)
        && !request.Headers.ContainsKey("Authorization")
        && !request.Headers.ContainsKey(AuthHeaders.ApiKey)
        && (request.Cookies.ContainsKey(AuthCookieExtensions.AccessTokenCookieName)
            || request.Cookies.ContainsKey(AuthCookieExtensions.RefreshTokenCookieName));
}
