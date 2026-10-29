using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Tests.Stubs;
using NamespaceRoot.ProductName.Common.Web.Authorization;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// Permissions asked from a separate service: its answer decides, it is reused for a short time, and a
/// permission service that does not answer is an outage rather than a refusal.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class RemotePermissionServiceTests
{
    private sealed class PermissionServiceStub(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls;
        public string? LastPath;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            LastPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(answer(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://permissions/") };
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (RemotePermissionService Service, PermissionServiceStub Stub) Create(
        Func<HttpRequestMessage, HttpResponseMessage> answer, UserContextMock? user = null, int cacheSeconds = 30)
    {
        var stub = new PermissionServiceStub(answer);
        var options = Options.Create(new PermissionsOptions
        {
            Source = "remote",
            Remote = new RemotePermissionsOptions { BaseAddress = "http://permissions/", CacheSeconds = cacheSeconds }
        });
        var service = new RemotePermissionService(new Factory(stub), user ?? new UserContextMock(),
            new MemoryCache(new MemoryCacheOptions()), options);
        return (service, stub);
    }

    [Test]
    public async Task A_permission_the_service_answers_lets_the_user_through()
    {
        var (service, stub) = Create(_ => Json("""["orders.read","orders.write"]"""));

        (await service.UserHasAllPermissionsAsync(["orders.read"], CancellationToken.None)).ShouldBeTrue();
        stub.LastPath.ShouldBe($"/api/v1/permissions/users/{TestDomainExecutionContext.DefaultTestUserId}");
    }

    [Test]
    public async Task A_permission_the_service_does_not_answer_is_refused()
    {
        var (service, _) = Create(_ => Json("""["orders.read"]"""));

        (await service.UserHasAllPermissionsAsync(["orders.read", "orders.delete"], CancellationToken.None)).ShouldBeFalse();
    }

    [Test]
    public async Task An_answer_is_reused_within_the_cache_time()
    {
        var (service, stub) = Create(_ => Json("""["orders.read"]"""));

        await service.UserHasAllPermissionsAsync(["orders.read"], CancellationToken.None);
        await service.UserHasAllPermissionsAsync(["orders.read"], CancellationToken.None);

        stub.Calls.ShouldBe(1);
    }

    [Test]
    public async Task A_permission_service_that_fails_is_an_outage_not_a_refusal()
    {
        var (service, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var failure = await Should.ThrowAsync<ServiceUnavailableException>(
            () => service.UserHasAllPermissionsAsync(["orders.read"], CancellationToken.None));
        failure.ErrorCode.ShouldBe("PERMISSIONS_UNAVAILABLE");
    }

    [Test]
    public async Task A_system_call_holds_every_permission_without_asking()
    {
        var system = new UserContextMock(authContext: AuthContextMock.CreateSystemAuth());
        var (service, stub) = Create(_ => Json("[]"), system);

        (await service.UserHasAllPermissionsAsync(["anything"], CancellationToken.None)).ShouldBeTrue();
        stub.Calls.ShouldBe(0);
    }
}
