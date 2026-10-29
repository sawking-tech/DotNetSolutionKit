using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;
using NamespaceRoot.ProductName.Common.Web.Authorization;
using NamespaceRoot.ProductName.Common.Web.Setup;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// The permission check every service gets from AddPlatformWebApi, with permissions read from the
/// token's claims.
/// </summary>
[TestFixture]
internal class PermissionAuthorizationTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddPlatformLogging();
        builder.AddPlatformWebApi(typeof(PermissionAuthorizationTests).Assembly,
            mvc => mvc.AddApplicationPart(typeof(PermissionAuthorizationTests).Assembly));
        builder.Services.AddHealthChecks();
        builder.Services
            .AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UsePlatformPipeline(typeof(PermissionAuthorizationTests).Assembly);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Test(Description = "An action without [RequiredPermissions] needs no permission")]
    public async Task Should_Allow_When_TheActionRequiresNothing()
    {
        var response = await SendAsync("/permission-probe/open", user: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test(Description = "An anonymous caller is challenged, not refused")]
    public async Task Should_Return401_When_TheCallerIsAnonymous()
    {
        var response = await SendAsync("/permission-probe/orders", user: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test(Description = "A user without the permission is refused with a problem naming it")]
    public async Task Should_Return403_When_ThePermissionIsMissing()
    {
        var response = await SendAsync("/permission-probe/orders", user: "reader", permissions: ["orders.view"]);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain("orders.edit");
    }

    [Test(Description = "A user holding every required permission gets through")]
    public async Task Should_Allow_When_EveryPermissionIsHeld()
    {
        var response = await SendAsync("/permission-probe/orders", user: "editor", permissions: ["orders.view", "orders.edit"]);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test(Description = "A system call holds every permission")]
    public async Task Should_Allow_When_TheCallIsASystemCall()
    {
        var response = await SendAsync("/permission-probe/orders", user: "platform", system: true);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> SendAsync(
        string path, string? user, string[]? permissions = null, bool system = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user is not null)
        {
            request.Headers.Add(HeaderAuthenticationHandler.UserHeader, user);
            foreach (var permission in permissions ?? [])
                request.Headers.Add(HeaderAuthenticationHandler.PermissionHeader, permission);
            if (system)
                request.Headers.Add(HeaderAuthenticationHandler.SystemHeader, "true");
        }

        return _client.SendAsync(request);
    }

    /// <summary>
    /// Authenticates from test headers, standing in for a token: the user name, each permission as a
    /// separate claim, and the system call marker.
    /// </summary>
    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestHeaders";
        public const string UserHeader = "X-Test-User";
        public const string PermissionHeader = "X-Test-Permission";
        public const string SystemHeader = "X-Test-System";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };
            claims.AddRange(Request.Headers[PermissionHeader].Select(p => new Claim(AuthClaims.Permissions, p!)));
            if (Request.Headers.ContainsKey(SystemHeader))
                claims.Add(new Claim(AuthClaims.AuthType, "System"));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

[ApiController]
[Route("permission-probe")]
public sealed class PermissionProbeController : ControllerBase
{
    [HttpGet("open")]
    public IActionResult Open() => Ok();

    [HttpGet("orders")]
    [RequiredPermissions("orders.view", "orders.edit")]
    public IActionResult Orders() => Ok();
}
