using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Configuration;

namespace NamespaceRoot.ProductName.Common.Web.Authentication;

/// <summary>
/// The token cookies of the login, refresh and logout flow, in one place, so no endpoint writes cookie
/// options of its own.
/// </summary>
/// <remarks>
/// <para>
/// Tokens go into HttpOnly cookies only, never into a response body, where a script on the page could
/// read them: login answers with the user, refresh with nothing, and any token error clears both cookies.
/// </para>
/// <para>
/// <c>SameSite</c> comes from <see cref="AuthCookieSettings"/>, which states the deployment; <c>Secure</c>
/// is set when the deployment is served over HTTPS or the request was. Cookie names are constants so the
/// authentication setup and the gateway read the same strings.
/// </para>
/// </remarks>
public static class AuthCookieExtensions
{
    /// <summary>
    /// Name of the access JWT cookie. Kept in sync with
    /// <see cref="ServiceAuthenticationSetup.AccessTokenCookieName"/> so the gateway JWT pipeline
    /// picks it up as a fallback for the Authorization header.
    /// </summary>
    public const string AccessTokenCookieName = ServiceAuthenticationSetup.AccessTokenCookieName;

    /// <summary>
    /// Name of the refresh token cookie. Restricted to the auth endpoints via
    /// <see cref="IRefreshTokenConfiguration.CookiePath"/>.
    /// </summary>
    public const string RefreshTokenCookieName = "refresh_token";

    /// <summary>
    /// Fixed path for the access token cookie. It must accompany every gateway request, so it
    /// lives at the root and is not restricted to the auth prefix like the refresh cookie.
    /// </summary>
    public const string AccessTokenCookiePath = "/";

    /// <summary>
    /// Writes the access JWT into an HttpOnly cookie with a lifetime aligned to
    /// <paramref name="jwtConfig"/>. Options mirror <see cref="SetRefreshTokenCookie"/>.
    /// </summary>
    public static void SetAccessTokenCookie(
        this HttpContext httpContext,
        string token,
        TimeProvider timeProvider,
        IJwtPublicConfiguration jwtConfig)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(jwtConfig);

        httpContext.Response.Cookies.Append(AccessTokenCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = IsSecure(httpContext),
            SameSite = Settings(httpContext).SameSiteFor(IsSecure(httpContext)),
            Path = AccessTokenCookiePath,
            Expires = timeProvider.GetUtcNow().AddMinutes(jwtConfig.LifetimeMinutes)
        });
    }

    /// <summary>
    /// Deletes the access token cookie. Must use the same <see cref="AccessTokenCookiePath"/>
    /// otherwise the browser will not delete the original entry.
    /// </summary>
    public static void ClearAccessTokenCookie(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.Cookies.Delete(AccessTokenCookieName, new CookieOptions
        {
            Path = AccessTokenCookiePath
        });
    }

    /// <summary>
    /// Writes the refresh token into an HttpOnly cookie scoped to
    /// <see cref="IRefreshTokenConfiguration.CookiePath"/>.
    /// </summary>
    public static void SetRefreshTokenCookie(
        this HttpContext httpContext,
        string token,
        TimeProvider timeProvider,
        IRefreshTokenConfiguration refreshConfig)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(refreshConfig);

        var path = string.IsNullOrWhiteSpace(refreshConfig.CookiePath) ? "/" : refreshConfig.CookiePath;

        httpContext.Response.Cookies.Append(RefreshTokenCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = IsSecure(httpContext),
            SameSite = Settings(httpContext).SameSiteFor(IsSecure(httpContext)),
            Path = path,
            Expires = timeProvider.GetUtcNow().AddDays(refreshConfig.ExpirationDays)
        });
    }

    /// <summary>
    /// Deletes the refresh token cookie. The <see cref="IRefreshTokenConfiguration.CookiePath"/>
    /// must match the one used at write time.
    /// </summary>
    public static void ClearRefreshTokenCookie(
        this HttpContext httpContext,
        IRefreshTokenConfiguration refreshConfig)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(refreshConfig);

        httpContext.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            Path = string.IsNullOrWhiteSpace(refreshConfig.CookiePath) ? "/" : refreshConfig.CookiePath
        });
    }

    /// <summary>
    /// Login and refresh: both tokens into their cookies. Nothing of them goes into the response body.
    /// </summary>
    public static void IssueTokenCookies(
        this HttpContext httpContext,
        string accessToken,
        string refreshToken,
        TimeProvider timeProvider,
        IJwtPublicConfiguration jwtConfig,
        IRefreshTokenConfiguration refreshConfig)
    {
        httpContext.SetAccessTokenCookie(accessToken, timeProvider, jwtConfig);
        httpContext.SetRefreshTokenCookie(refreshToken, timeProvider, refreshConfig);
    }

    /// <summary>The refresh token the browser sent, or <c>null</c>. Refresh reads it from here alone.</summary>
    public static string? ReadRefreshTokenCookie(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var token = httpContext.Request.Cookies[RefreshTokenCookieName];
        return string.IsNullOrEmpty(token) ? null : token;
    }

    /// <summary>Logout, and any token error: both cookies go.</summary>
    public static void ClearTokenCookies(this HttpContext httpContext, IRefreshTokenConfiguration refreshConfig)
    {
        httpContext.ClearAccessTokenCookie();
        httpContext.ClearRefreshTokenCookie(refreshConfig);
    }

    private static AuthCookieSettings Settings(HttpContext httpContext) =>
        httpContext.RequestServices?.GetService<AuthCookieSettings>() ?? new AuthCookieSettings();

    /// <summary>
    /// HTTPS by the deployment's word, or a request that arrived over it, directly or through a proxy that
    /// says so in <c>X-Forwarded-Proto</c>.
    /// </summary>
    private static bool IsSecure(HttpContext httpContext) =>
        Settings(httpContext).ServedOverHttps
        || httpContext.Request.IsHttps
        || string.Equals(httpContext.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase);
}
