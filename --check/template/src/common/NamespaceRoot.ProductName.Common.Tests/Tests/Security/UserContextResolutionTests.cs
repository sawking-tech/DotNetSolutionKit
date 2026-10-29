using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Execution;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Security;

/// <summary>
/// Which actor a resolved <see cref="IUserContext"/> stands for: the HTTP caller inside a request, the
/// actor a job or a message carries outside one, and the system when neither is present.
/// </summary>
[TestFixture]
internal class UserContextResolutionTests
{
    private static readonly Guid CallerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TriggeredById = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static IUserContext Resolve(HttpContext? httpContext = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddExecutionContext();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IUserContext>();
    }

    [Test(Description = "Inside a request the actor is the authenticated caller")]
    public void Should_ResolveTheCaller_When_ThereIsAnHttpRequest()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthClaims.UserId, CallerId.ToString())], "Test"))
        };

        Resolve(http).UserId.ShouldBe(CallerId);
    }

    [Test(Description = "A job or a consumer started on someone's behalf runs as that actor")]
    public void Should_ResolveTheCarriedActor_When_AJobRunsOnSomeonesBehalf()
    {
        using var _ = JobActorContext.Use(new JobTriggeredByUserContext(TriggeredById, "operator@example.com", tenantId: null));

        Resolve().UserId.ShouldBe(TriggeredById);
    }

    [Test(Description = "Work with no caller and no carried actor runs as the system instead of failing")]
    public void Should_ResolveTheSystem_When_NothingCarriesAnActor()
    {
        var actor = Resolve();

        actor.ShouldBeSameAs(SystemUserContext.Instance);
        actor.AuthContext.Type.ShouldBe(AuthMethod.System);
    }

    [Test(Description = "Work no person asked for acts as the system, even inside a job someone enqueued")]
    public void Should_ActAsTheSystem_When_TheSystemContextIsAskedFor()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddExecutionContext();
        using var provider = services.BuildServiceProvider();
        using var _ = JobActorContext.Use(new JobTriggeredByUserContext(TriggeredById, "operator@example.com", tenantId: null));

        var context = provider.GetRequiredService<ISystemExecutionContextFactory>().Create();

        context.Actor.ShouldBeSameAs(SystemUserContext.Instance);
        context.TimeProvider.ShouldBeSameAs(TimeProvider.System);
    }
}
