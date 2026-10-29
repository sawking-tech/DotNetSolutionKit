using Microsoft.AspNetCore.Http;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;

namespace NamespaceRoot.ProductName.Common.Web.Authentication;

/// <summary>
/// How the token cookies are sent, stated by configuration for the deployment the service runs in.
/// </summary>
/// <remarks>
/// The request alone cannot tell: a frontend on the same site over HTTPS needs <c>Lax</c>, and one on
/// another domain needs <c>None</c>, which browsers accept only with <c>Secure</c>, that is over HTTPS. A
/// guess from the request gave the first <c>None</c>, losing the protection of <c>Lax</c>, and gave the
/// second no cookie at all over HTTP. See docs/architecture/authentication-and-permissions.md.
/// <para>
/// Without the section the cookies behave as in solutions generated before it existed: <c>SameSite</c>
/// from the request and no CSRF check, so updating <c>Common</c> changes nothing for a running frontend. A
/// service generated now carries the section, with <c>Lax</c> and the check on.
/// </para>
/// </remarks>
public sealed class AuthCookieSettings
{
    public const string SectionName = "AuthCookies";

    /// <summary>
    /// <c>Lax</c>, <c>Strict</c>, or <c>None</c> for a frontend on another domain. Not set: <c>None</c> over
    /// HTTPS and <c>Lax</c> over HTTP, as before the setting existed.
    /// </summary>
    public SameSiteMode? SameSite { get; set; }

    /// <summary>
    /// Whether a state-changing request a token cookie would authenticate needs the CSRF header. On in a
    /// service generated now; off without the section, as before it existed.
    /// </summary>
    public bool RequireCsrfHeader { get; set; }

    /// <summary>
    /// Whether clients reach the service over HTTPS, terminated here or at a proxy in front. The cookies
    /// are then always <c>Secure</c>; otherwise they are <c>Secure</c> when the request was.
    /// </summary>
    public bool ServedOverHttps { get; set; }

    /// <summary>The <c>SameSite</c> of a cookie written for this request.</summary>
    public SameSiteMode SameSiteFor(bool secure) => SameSite ?? (secure ? SameSiteMode.None : SameSiteMode.Lax);

    /// <summary>
    /// Refuses a combination the browser or the CSRF check cannot honour, before the service starts.
    /// </summary>
    public void Validate(ICorsSettings? cors)
    {
        if (SameSite != SameSiteMode.None)
            return;

        if (!ServedOverHttps)
        {
            throw new ConfigurationException(
                "AuthCookies:SameSite=None needs HTTPS: browsers drop a SameSite=None cookie that is not Secure. " +
                "Serve over HTTPS and set AuthCookies:ServedOverHttps=true, or put the frontend and the API " +
                "behind one domain (a reverse proxy or the gateway) and keep SameSite=Lax.");
        }

        if (cors is { AllowCredentials: true } && cors.AllowedOrigins.Contains("*"))
        {
            throw new ConfigurationException(
                "AuthCookies:SameSite=None with Cors:AllowedOrigins=* and credentials lets any site send the " +
                "token cookies and the CSRF header with them. List the frontend's origins in Cors:AllowedOrigins.");
        }
    }
}
