using MassTransit;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// MassTransit consume filter that sets the ambient DI scope for domain event infrastructure resolution.
/// Enables singleton EF interceptors to resolve scoped services (e.g. <c>IDomainEventStorage</c>)
/// during message consumption without accessing the root provider.
/// </summary>
/// <remarks>
/// Registered globally via <c>UseConsumeFilter</c> in <c>AddMessaging</c>.
/// One instance is created per message from the consumer's per-message DI scope.
/// </remarks>
internal sealed class DomainEventScopeFilter<T>(IServiceProvider serviceProvider) : IFilter<ConsumeContext<T>>
    where T : class
{
    /// <inheritdoc />
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        using (DomainEventScopeContext.Use(serviceProvider))
            await next.Send(context);
    }

    /// <inheritdoc />
    public void Probe(ProbeContext context) { }
}
