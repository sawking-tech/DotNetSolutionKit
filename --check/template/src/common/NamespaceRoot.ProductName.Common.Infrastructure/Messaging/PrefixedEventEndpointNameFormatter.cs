using NamespaceRoot.ProductName.Common.Domain.Messaging;
using MassTransit;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

/// <summary>
/// Endpoint formatter that prefixes <em>event</em> consumer queues with a per-service token
/// (e.g. <c>orders-order-placed</c>) while leaving <em>command</em> consumer queues
/// prefix-free (e.g. <c>send-email-command-v1</c>).
/// </summary>
/// <remarks>
/// Two services with identically named <c>IConsumer&lt;TEvent&gt;</c> classes would otherwise
/// share a single queue and silently round-robin events; the prefix keeps event queues isolated.
/// Commands are point-to-point and must stay prefix-free so cross-service
/// <c>EndpointConvention.Map&lt;TCommand&gt;</c> targets the same address no matter who sends.
/// The format decision is driven by which interface the message implements
/// (<see cref="IBusCommand"/> vs <see cref="IBusEvent"/>), so callers do not need to put
/// <c>[EndpointName]</c> attributes on consumer classes.
/// </remarks>
internal sealed class PrefixedEventEndpointNameFormatter : IEndpointNameFormatter
{
    private readonly KebabCaseEndpointNameFormatter _prefixed;
    private readonly KebabCaseEndpointNameFormatter _noPrefix = new(false);

    public PrefixedEventEndpointNameFormatter(string servicePrefix)
    {
        _prefixed = new KebabCaseEndpointNameFormatter(servicePrefix, false);
    }

    public string Separator => _prefixed.Separator;

    public string TemporaryEndpoint(string tag) => _prefixed.TemporaryEndpoint(tag);

    public string Consumer<T>() where T : class, IConsumer
    {
        // MassTransit calls Consumer<T>() once per consumer class to derive its receive endpoint
        // name. We override the default for command consumers so they sit on the prefix-free
        // queue that EndpointConvention.Map<TCommand> registers cross-service.
        var commandMessage = typeof(T).GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault(m => typeof(IBusCommand).IsAssignableFrom(m));

        return commandMessage is not null
            ? _noPrefix.SanitizeName(commandMessage.Name)
            : _prefixed.Consumer<T>();
    }

    public string Message<T>() where T : class
    {
        return typeof(IBusCommand).IsAssignableFrom(typeof(T))
            ? _noPrefix.Message<T>()
            : _prefixed.Message<T>();
    }

    public string Saga<T>() where T : class, ISaga => _prefixed.Saga<T>();

    public string ExecuteActivity<T, TArguments>()
        where T : class, IExecuteActivity<TArguments>
        where TArguments : class
        => _prefixed.ExecuteActivity<T, TArguments>();

    public string CompensateActivity<T, TLog>()
        where T : class, ICompensateActivity<TLog>
        where TLog : class
        => _prefixed.CompensateActivity<T, TLog>();

    public string SanitizeName(string name) => _prefixed.SanitizeName(name);
}
