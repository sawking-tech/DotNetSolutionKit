using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Infrastructure.Http.Internal;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Http;

/// <summary>
/// A call from one service of the product to another goes with the internal key, the person it is made
/// for and the correlation identifier - or the called service sees an anonymous stranger, checks no
/// permission of the right person, and logs under an identifier nobody searches by.
/// </summary>
[TestFixture]
internal class InternalServiceHttpTests
{
    private const string InternalKey = "test-do-not-use-internal-key";
    private const string UserId = "11111111-1111-1111-1111-111111111111";
    private const string TenantId = "22222222-2222-2222-2222-222222222222";

    [Test]
    public async Task The_person_goes_with_their_tenant_roles_and_permissions()
    {
        var sent = await Call(Authenticated(
            (AuthClaims.UserId, UserId),
            (AuthClaims.AuthType, "Jwt"),
            (AuthClaims.Jti, "token-1"),
            (AuthClaims.UserLogin, "operator@example.com"),
            (AuthClaims.TenantId, TenantId),
            (AuthClaims.UserRole, "Admin"),
            (AuthClaims.Permissions, "orders.read"),
            (AuthClaims.Permissions, "orders.write")));

        Header(sent, AuthHeaders.ApiKey).ShouldBe(InternalKey);
        Header(sent, AuthHeaders.UserId).ShouldBe(UserId);
        Header(sent, AuthHeaders.AuthId).ShouldBe("token-1");
        Header(sent, AuthHeaders.UserLogin).ShouldBe("operator@example.com");
        Header(sent, AuthHeaders.TenantId).ShouldBe(TenantId);
        Header(sent, AuthHeaders.UserRoles).ShouldBe("Admin");
        Header(sent, AuthHeaders.UserPermissions).ShouldBe("orders.read,orders.write",
            "the called service checks its permissions against these, as it does against the gateway's");
        Header(sent, AuthHeaders.SystemCall).ShouldBeNull();
    }

    [Test]
    public async Task Work_with_no_request_behind_it_calls_as_the_system()
    {
        var sent = await Call(user: null);

        Header(sent, AuthHeaders.ApiKey).ShouldBe(InternalKey);
        Header(sent, AuthHeaders.SystemCall).ShouldBe("true");
        Header(sent, AuthHeaders.UserId).ShouldBeNull();
    }

    [Test]
    public async Task A_system_caller_stays_the_system()
    {
        var sent = await Call(Authenticated((AuthClaims.AuthType, "System"), (AuthClaims.UserId, Guid.Empty.ToString())));

        Header(sent, AuthHeaders.SystemCall).ShouldBe("true");
        Header(sent, AuthHeaders.UserId).ShouldBeNull();
    }

    [Test]
    public async Task An_anonymous_request_does_not_become_the_system()
    {
        var sent = await Call(new ClaimsPrincipal(new ClaimsIdentity()));

        Header(sent, AuthHeaders.SystemCall).ShouldBeNull(
            "an open endpoint would otherwise act with the system's rights in the next service");
        Header(sent, AuthHeaders.UserId).ShouldBeNull();
    }

    [Test]
    public async Task A_line_break_in_a_claim_does_not_start_a_header_of_its_own()
    {
        var sent = await Call(Authenticated(
            (AuthClaims.UserId, UserId),
            (AuthClaims.DisplayName, "Eve\r\nX-System-Call: true")));

        Header(sent, AuthHeaders.SystemCall).ShouldBeNull();
        Header(sent, AuthHeaders.UserDisplayName).ShouldBe("EveX-System-Call: true");
    }

    [Test]
    public async Task The_correlation_of_the_running_work_goes_with_the_call()
    {
        using var _ = Correlation.Use("client-chosen-42");

        var sent = await Call(user: null);

        Header(sent, TracingHeaders.CorrelationId).ShouldBe("client-chosen-42");
    }

    [Test]
    public async Task A_correlation_set_by_the_caller_is_kept()
    {
        using var _ = Correlation.Use("client-chosen-42");

        var sent = await Call(user: null, request => request.Headers.Add(TracingHeaders.CorrelationId, "explicit"));

        Header(sent, TracingHeaders.CorrelationId).ShouldBe("explicit");
    }

    // --- plumbing -----------------------------------------------------------------------------

    private static ClaimsPrincipal Authenticated(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Test"));

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    /// <summary>
    /// Sends through a client built by the factory, as a service builds it, with the request's user on the
    /// accessor - the factory resolves the handlers in a scope of its own, so this is also what proves the
    /// person reaches them.
    /// </summary>
    private static async Task<HttpRequestMessage> Call(ClaimsPrincipal? user, Action<HttpRequestMessage>? prepare = null)
    {
        var accessor = new HttpContextAccessor { HttpContext = user is null ? null : new DefaultHttpContext { User = user } };
        var recorder = new RecordingHandler();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddSingleton(Mock.Of<IInternalApiConfiguration>(c => c.ApiKey == InternalKey));
        services.AddHttpClient("orders", c => c.BaseAddress = new Uri("http://orders"))
            .ConfigurePrimaryHttpMessageHandler(() => recorder)
            .AddInternalServiceHandlers();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("orders");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/orders");
        prepare?.Invoke(request);
        await client.SendAsync(request);

        accessor.HttpContext = null;
        return recorder.Sent!;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
