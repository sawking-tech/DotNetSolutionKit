using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Moq;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;
using NamespaceRoot.ProductName.Common.Web.Authentication;
using NamespaceRoot.ProductName.Common.Web.Setup;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// Tokens travel in cookies, and what the cookie can be depends on the deployment: the rows of the table
/// in the authentication docs, and the CSRF check a cookie needs.
/// </summary>
[TestFixture]
internal class CookieAuthenticationTests
{
    private static readonly IJwtPublicConfiguration Jwt =
        Mock.Of<IJwtPublicConfiguration>(c => c.LifetimeMinutes == 15);

    private static readonly IRefreshTokenConfiguration Refresh =
        Mock.Of<IRefreshTokenConfiguration>(c => c.ExpirationDays == 7 && c.CookiePath == "/api/v1/auth");

    /// <summary>The section a service generated now carries.</summary>
    private static readonly (string, string)[] Generated =
        [("AuthCookies:SameSite", "Lax"), ("AuthCookies:RequireCsrfHeader", "true")];

    private static Task<WebApplication> StartAsync(params (string Key, string Value)[] settings) =>
        StartCoreAsync([.. Generated, .. settings]);

    /// <summary>A service generated before the section existed.</summary>
    private static Task<WebApplication> StartWithoutSectionAsync() => StartCoreAsync([]);

    private static async Task<WebApplication> StartCoreAsync((string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Cors:AllowedOrigins:0"] = "https://app.example.com";
        foreach (var (key, value) in settings)
            builder.Configuration[key] = value;
        builder.AddPlatformLogging();
        builder.AddPlatformWebApi(typeof(CookieAuthenticationTests).Assembly);

        var app = builder.Build();
        app.UsePlatformPipeline(typeof(CookieAuthenticationTests).Assembly, authenticate: false);
        app.MapPost("/login", (HttpContext context) =>
        {
            context.IssueTokenCookies("access", "refresh", TimeProvider.System, Jwt, Refresh);
            return Results.Ok(new { name = "operator" });
        });
        app.MapPost("/orders", () => Results.Ok());
        app.MapGet("/orders", () => Results.Ok());
        await app.StartAsync();
        return app;
    }

    private static string AccessCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthCookieExtensions.AccessTokenCookieName + "="));

    // --- the deployment table -------------------------------------------------------------------------

    [Test]
    public async Task Same_site_by_default_the_cookie_is_Lax_and_HttpOnly()
    {
        await using var app = await StartAsync();

        var cookie = AccessCookie(await app.GetTestClient().PostAsync("/login", null));

        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldNotContain("secure", Case.Insensitive, "a plain-HTTP request on a deployment that did not say HTTPS");
    }

    [Test]
    public async Task Another_domain_over_HTTPS_gets_None_and_Secure()
    {
        await using var app = await StartAsync(("AuthCookies:SameSite", "None"), ("AuthCookies:ServedOverHttps", "true"));

        var cookie = AccessCookie(await app.GetTestClient().PostAsync("/login", null));

        cookie.ShouldContain("samesite=none", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
    }

    [Test]
    public async Task Another_domain_without_HTTPS_refuses_to_start()
    {
        var refused = await Should.ThrowAsync<ConfigurationException>(() => StartAsync(("AuthCookies:SameSite", "None")));

        refused.Message.ShouldContain("one domain",
            customMessage: "the browser would drop the cookie; the message names what to do instead");
    }

    [Test]
    public async Task None_with_any_origin_allowed_refuses_to_start()
    {
        await Should.ThrowAsync<ConfigurationException>(() => StartAsync(
            ("AuthCookies:SameSite", "None"),
            ("AuthCookies:ServedOverHttps", "true"),
            ("Cors:AllowedOrigins:0", "*")), "any site could then send the cookie and the CSRF header");
    }

    // --- a solution generated before the section: nothing changes for its frontend --------------------

    [Test]
    public async Task Without_the_section_the_cookie_follows_the_request_as_before()
    {
        await using var app = await StartWithoutSectionAsync();
        var client = app.GetTestClient();

        AccessCookie(await client.PostAsync("/login", null)).ShouldContain("samesite=lax", Case.Insensitive);

        using var overHttps = new HttpRequestMessage(HttpMethod.Post, "/login");
        overHttps.Headers.Add("X-Forwarded-Proto", "https");
        AccessCookie(await client.SendAsync(overHttps)).ShouldContain("samesite=none", Case.Insensitive);
    }

    [Test]
    public async Task Without_the_section_no_CSRF_header_is_required()
    {
        await using var app = await StartWithoutSectionAsync();

        var response = await app.GetTestClient().SendAsync(Post(cookie: AuthCookieExtensions.AccessTokenCookieName));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, "a frontend that never sent the header keeps working after Common is updated");
    }

    // --- CSRF -------------------------------------------------------------------------------------------

    private static HttpRequestMessage Post(string? cookie = null, bool csrf = false, string? authorization = null, string method = "POST")
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/orders");
        if (cookie is not null) request.Headers.Add("Cookie", $"{cookie}=value");
        if (csrf) request.Headers.Add(CsrfProtectionMiddleware.HeaderName, "1");
        if (authorization is not null) request.Headers.Add("Authorization", authorization);
        return request;
    }

    [Test]
    public async Task A_cookie_authenticated_POST_without_the_header_is_refused()
    {
        await using var app = await StartAsync();

        var response = await app.GetTestClient().SendAsync(Post(cookie: AuthCookieExtensions.AccessTokenCookieName));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain(CsrfProtectionMiddleware.MissingHeaderCode);
    }

    [Test]
    public async Task The_refresh_cookie_alone_needs_the_header_too()
    {
        await using var app = await StartAsync();

        var response = await app.GetTestClient().SendAsync(Post(cookie: AuthCookieExtensions.RefreshTokenCookieName));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "refresh is a cookie-only POST, the first a forged page would try");
    }

    [Test]
    public async Task With_the_header_the_request_goes_through()
    {
        await using var app = await StartAsync();

        var response = await app.GetTestClient().SendAsync(Post(cookie: AuthCookieExtensions.AccessTokenCookieName, csrf: true));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_request_with_an_Authorization_header_or_an_API_key_is_not_affected()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        (await client.SendAsync(Post(authorization: "Bearer abc"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var withKey = Post(cookie: AuthCookieExtensions.AccessTokenCookieName);
        withKey.Headers.Add(AuthHeaders.ApiKey, "key");
        (await client.SendAsync(withKey)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_safe_method_needs_no_header()
    {
        await using var app = await StartAsync();

        var response = await app.GetTestClient().SendAsync(Post(cookie: AuthCookieExtensions.AccessTokenCookieName, method: "GET"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
