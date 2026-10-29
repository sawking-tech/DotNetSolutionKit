using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Events;

/// <summary>
/// a Hangfire job runs with no HTTP context and outside the MassTransit consume filter, so
/// without an explicitly published scope the domain-event interceptors resolve nothing and DROP the
/// job's events. These tests pin the contract that makes the job's scope visible.
/// </summary>
[TestFixture]
[TestOf(typeof(DomainEventJobActivator))]
[Parallelizable(ParallelScope.All)]
public class DomainEventJobActivatorTests
{
    private sealed class Probe;

    private static DomainEventJobActivator CreateActivator(out ServiceProvider root)
    {
        var services = new ServiceCollection();
        services.AddScoped<Probe>();
        root = services.BuildServiceProvider();
        return new DomainEventJobActivator(root.GetRequiredService<IServiceScopeFactory>());
    }

    [Test(Description = "The job's DI scope is published as the ambient domain-event scope for the whole " +
                        "invocation - this is what stops a background job's events from being silently dropped.")]
    public async Task Should_PublishAmbientScope_When_JobScopeBegins()
    {
        var activator = CreateActivator(out var root);
        await using var _ = root;

        using var scope = activator.BeginScope((JobActivatorContext)null!);

        DomainEventScopeContext.Current.ShouldNotBeNull();
        // Resolving through the ambient scope must hit the SAME container the job resolves from,
        // otherwise a handler would write through a different DbContext than the job's.
        DomainEventScopeContext.Current!.GetService<Probe>().ShouldBeSameAs(scope.Resolve(typeof(Probe)));
    }

    [Test(Description = "Disposing the job scope restores the previous ambient scope, so a worker thread " +
                        "reused by the next job never inherits a disposed container.")]
    public async Task Should_RestorePreviousScope_When_JobScopeDisposed()
    {
        var activator = CreateActivator(out var root);
        await using var _ = root;

        DomainEventScopeContext.Current.ShouldBeNull();
        var scope = activator.BeginScope((JobActivatorContext)null!);
        DomainEventScopeContext.Current.ShouldNotBeNull();

        scope.Dispose();

        DomainEventScopeContext.Current.ShouldBeNull();
    }
}
