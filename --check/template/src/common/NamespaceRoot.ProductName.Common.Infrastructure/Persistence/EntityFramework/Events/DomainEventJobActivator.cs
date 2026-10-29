using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Hangfire job activator that publishes the job's DI scope as the ambient
/// <see cref="DomainEventScopeContext"/> for the whole duration of the job.
/// </summary>
/// <remarks>
/// Without this, a background job is a self-managed processing scope with no HTTP context, so
/// <c>DomainEventInfrastructureResolver</c> finds no service provider and the interceptors DROP every
/// domain event the job raises - silently, since a dropped event is not an error: the job commits its
/// change, and every handler that should have followed it never runs.
/// MassTransit consumers get the same treatment from <see cref="DomainEventScopeFilter{T}"/>;
/// this is its Hangfire counterpart, and it covers every job at once rather than asking each one to
/// remember the wrapper.
/// </remarks>
public sealed class DomainEventJobActivator(IServiceScopeFactory scopeFactory) : JobActivator
{
    /// <inheritdoc />
    public override JobActivatorScope BeginScope(JobActivatorContext context) =>
        new DomainEventJobActivatorScope(scopeFactory.CreateScope());

    private sealed class DomainEventJobActivatorScope : JobActivatorScope
    {
        private readonly IServiceScope _serviceScope;
        private readonly IDisposable _ambientScope;

        public DomainEventJobActivatorScope(IServiceScope serviceScope)
        {
            _serviceScope = serviceScope;
            // Set inside BeginScope, which Hangfire calls synchronously on the frame that then invokes
            // the job method - an AsyncLocal write here is visible to the whole invocation below it.
            _ambientScope = DomainEventScopeContext.Use(serviceScope.ServiceProvider);
        }

        public override object Resolve(Type type) =>
            ActivatorUtilities.GetServiceOrCreateInstance(_serviceScope.ServiceProvider, type);

        public override void DisposeScope()
        {
            _ambientScope.Dispose();
            _serviceScope.Dispose();
        }
    }
}
