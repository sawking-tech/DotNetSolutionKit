using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;
using NamespaceRoot.ProductName.Common.Web.Gateway;
using System.Text.Encodings.Web;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// What a gateway passes on about the caller: nothing a client set itself, and for an authenticated
/// caller the internal key with the context read from the token.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class GatewayForwardingTests
{
    private const string InternalKey = "test-do-not-use-internal-key";

    private static HttpRequestMessage Outgoing(params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://orders/api/v1/orders");
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }

    private static ClaimsPrincipal Authenticated(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Bearer"));

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    [Test(Description = "Context headers a client sent are removed, so nobody can claim to be someone else")]
    public void Should_RemoveSpoofedContext_When_TheCallerIsAnonymous()
    {
        var request = Outgoing(
            (AuthHeaders.UserId, "00000000-0000-0000-0000-000000000001"),
            (AuthHeaders.SystemCall, "true"),
            (AuthHeaders.UserPermissions, "orders.delete"),
            (AuthHeaders.ApiKey, "a-key-the-client-sent"));

        GatewayForwarding.ForwardUser(request.Headers, new ClaimsPrincipal(new ClaimsIdentity()), InternalKey);

        foreach (var header in AuthHeaders.UserContext.Append(AuthHeaders.ApiKey))
            Header(request, header).ShouldBeNull(header);
    }

    [Test(Description = "An authenticated caller is forwarded with the internal key and the context from the token")]
    public void Should_ForwardTheUser_When_TheCallerIsAuthenticated()
    {
        var request = Outgoing((AuthHeaders.UserId, "spoofed"));
        var user = Authenticated(
            (AuthClaims.UserId, "11111111-1111-1111-1111-111111111111"),
            (AuthClaims.UserLogin, "operator@example.com"),
            (AuthClaims.Jti, "token-1"),
            (AuthClaims.UserRole, "Admin"),
            (AuthClaims.Permissions, "orders.read"),
            (AuthClaims.Permissions, "orders.write"));

        GatewayForwarding.ForwardUser(request.Headers, user, InternalKey);

        Header(request, AuthHeaders.ApiKey).ShouldBe(InternalKey);
        Header(request, AuthHeaders.UserId).ShouldBe("11111111-1111-1111-1111-111111111111");
        Header(request, AuthHeaders.UserLogin).ShouldBe("operator@example.com");
        Header(request, AuthHeaders.AuthId).ShouldBe("token-1");
        Header(request, AuthHeaders.UserRoles).ShouldBe("Admin");
        Header(request, AuthHeaders.UserPermissions).ShouldBe("orders.read,orders.write");
    }

    [Test(Description = "A line break in a claim cannot start a header of its own")]
    public void Should_StripLineBreaksFromClaims()
    {
        var request = Outgoing();

        GatewayForwarding.ForwardUser(request.Headers,
            Authenticated((AuthClaims.UserId, "u1"), (AuthClaims.UserLogin, "evil\r\nX-System-Call: true")), InternalKey);

        Header(request, AuthHeaders.UserLogin).ShouldBe("evilX-System-Call: true");
        Header(request, AuthHeaders.SystemCall).ShouldBeNull();
    }

    [Test(Description = "Without an internal key configured nothing is forwarded: the services would refuse it anyway")]
    public void Should_ForwardNothing_When_NoInternalKeyIsConfigured()
    {
        var request = Outgoing();

        GatewayForwarding.ForwardUser(request.Headers, Authenticated((AuthClaims.UserId, "u1")), internalApiKey: "");

        Header(request, AuthHeaders.ApiKey).ShouldBeNull();
        Header(request, AuthHeaders.UserId).ShouldBeNull();
    }

    private sealed class TokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        // "valid" authenticates; any other bearer token fails, as an expired or forged one would.
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            return Task.FromResult(header == "Bearer valid"
                ? AuthenticateResult.Success(new AuthenticationTicket(Authenticated((AuthClaims.UserId, "u1")), "Test"))
                : AuthenticateResult.Fail("invalid token"));
        }
    }

    private static async Task<HttpStatusCode> Send(string? bearer)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TokenHandler>("Test", _ => { });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseRejectInvalidCredentials();
        app.Run(context => context.Response.WriteAsync("forwarded"));
        await app.StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/orders");
        if (bearer != null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {bearer}");
        return (await app.GetTestClient().SendAsync(request)).StatusCode;
    }

    [Test(Description = "A rejected token stops at the gateway with 401")]
    public async Task Should_Refuse_When_ThePresentedTokenIsInvalid() =>
        (await Send("expired")).ShouldBe(HttpStatusCode.Unauthorized);

    [Test(Description = "A valid token goes on")]
    public async Task Should_PassOn_When_TheTokenIsValid() =>
        (await Send("valid")).ShouldBe(HttpStatusCode.OK);

    [Test(Description = "No token goes on anonymously: the service decides whether the endpoint is public")]
    public async Task Should_PassOn_When_NoTokenIsPresented() =>
        (await Send(null)).ShouldBe(HttpStatusCode.OK);
}
