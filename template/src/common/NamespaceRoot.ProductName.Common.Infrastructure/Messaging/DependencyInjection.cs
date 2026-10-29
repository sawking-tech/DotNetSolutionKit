using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Application.Messaging;
using NamespaceRoot.ProductName.Common.Contracts;
using NamespaceRoot.ProductName.Common.Contracts.Health;
using NamespaceRoot.ProductName.Common.Domain.Messaging;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

/// <summary>
/// Extension methods for registering message bus infrastructure into the DI container.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMethodReturnValue.Local")]
public static class DependencyInjection
{
    /// <summary>
    /// Registers messaging without Transactional Outbox.
    /// Messages are sent directly to RabbitMQ on publish.
    /// Suitable for services where eventual consistency is acceptable.
    /// When RabbitMq:Enabled is false — registers NoOpMessageBus, skips transport entirely.
    /// </summary>
    /// <param name="servicePrefix">
    /// Kebab-case prefix prepended to receive endpoint names (e.g. <c>orders</c> → <c>orders-order-placed</c>).
    /// Required to keep event queues isolated across services — two services with identically
    /// named <c>IConsumer&lt;T&gt;</c> classes would otherwise share one queue and round-robin events.
    /// Command routing stays prefix-free so cross-service send addresses remain stable.
    /// </param>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        string servicePrefix,
        params Assembly[] consumerAssemblies)
    {
        return services.AddMessagingCore(configuration, servicePrefix, null, consumerAssemblies);
    }

    /// <summary>
    /// Registers messaging with Transactional Outbox.
    /// Messages are saved to DB within the same transaction as business data,
    /// then delivered to RabbitMQ by a background worker.
    /// Suitable for services where message loss is unacceptable.
    /// When RabbitMq:Enabled is false — registers NoOpMessageBus, skips transport entirely.
    /// </summary>
    /// <param name="servicePrefix">
    /// Kebab-case prefix prepended to receive endpoint names (e.g. <c>orders</c> → <c>orders-order-placed</c>).
    /// Required to keep event queues isolated across services — two services with identically
    /// named <c>IConsumer&lt;T&gt;</c> classes would otherwise share one queue and round-robin events.
    /// Command routing stays prefix-free so cross-service send addresses remain stable.
    /// </param>
    public static IServiceCollection AddMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string servicePrefix,
        params Assembly[] consumerAssemblies)
        where TDbContext : DbContext
    {
        return services.AddMessagingCore(configuration, servicePrefix, typeof(TDbContext), consumerAssemblies);
    }

    private static IServiceCollection AddMessagingCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string servicePrefix,
        Type? dbContextType,
        Assembly[] consumerAssemblies)
    {
        if (string.IsNullOrWhiteSpace(servicePrefix))
            throw new ArgumentException(
                "Service prefix is required so receive queues stay isolated across services.",
                nameof(servicePrefix));

        var settings = configuration
            .GetSection(RabbitMqSettings.SectionName)
            .Get<RabbitMqSettings>();

        if (settings is not { Enabled: true })
        {
            // Make this loud — silently registering a no-op bus is a footgun. With this hosted
            // service the startup log prints a single CRITICAL line per service so operators
            // see "outbox/bus is OFF" before any message is silently dropped at runtime.
            services.AddScoped<IMessageBus, NoOpMessageBus>();
            services.AddHostedService<NoOpMessageBusStartupAnnouncer>();
            return services;
        }

        services.ValidateOptions<RabbitMqSettings>(RabbitMqSettings.SectionName);
        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        // MassTransit's own check sees only receive endpoints; this one sees the broker.
        services.AddHealthChecks().AddCheck(
            "rabbitmq",
            new RabbitMqConnectionHealthCheck(settings),
            tags: [HealthConstants.ReadyTag]);

        // Diagnostic state + announcers — registered unconditionally so DI shape is stable.
        // The announcers themselves read Diagnostics:Messaging:LogStartup at runtime and
        // skip logging when the flag is off (which is the default). Flip the flag in
        // appsettings / env (Diagnostics__Messaging__LogStartup=true) during an
        // investigation; turn it off once the issue is understood.
        services.AddSingleton(new OutboxRegistrationState { DbContextType = dbContextType });
        services.AddHostedService<MessagingSetupAnnouncer>();
        if (dbContextType is not null)
            services.AddSingleton(typeof(IHostedService),
                typeof(OutboxStartupAnnouncer<>).MakeGenericType(dbContextType));

        services.AddRabbitMqTransport(settings, servicePrefix, dbContextType, consumerAssemblies);

        return services;
    }

    private static IServiceCollection AddRabbitMqTransport(
        this IServiceCollection services,
        IRabbitMqSettings rabbitSettings,
        string servicePrefix,
        Type? dbContextType,
        Assembly[] consumerAssemblies)
    {
        ValidateCommandConsumers(consumerAssemblies);
        ValidateCommandConsumerNames(consumerAssemblies);

        services.AddScoped(typeof(DomainEventScopeFilter<>));
        services.AddScoped(typeof(ActorRestoreConsumeFilter<>));
        services.AddScoped(typeof(ActorPropagationPublishFilter<>));
        services.AddScoped(typeof(ActorPropagationSendFilter<>));

        services.AddMassTransit(bus =>
        {
            foreach (var assembly in consumerAssemblies)
            {
                bus.AddConsumers(assembly);
            }

            // Event consumer queues get a per-service prefix (orders-order-placed etc.)
            // so identically named consumer classes in different services do not share one
            // queue and silently round-robin events. Command consumer queues stay prefix-free
            // so cross-service EndpointConvention.Map<TCommand> lands on the same address
            // regardless of which service registered the consumer — see
            // PrefixedEventEndpointNameFormatter for the per-message decision.
            bus.SetEndpointNameFormatter(new PrefixedEventEndpointNameFormatter(servicePrefix));

            if (dbContextType is not null)
            {
                ConfigureOutbox(bus, dbContextType);
            }

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(rabbitSettings.Host, host =>
                {
                    host.Username(rabbitSettings.UserName);
                    host.Password(rabbitSettings.Password);
                });

                rabbit.PurgeOnStartup = rabbitSettings.PurgeOnStartup;

                rabbit.UseConsumeFilter(typeof(DomainEventScopeFilter<>), context);

                // Who asked for the work travels with the message and is put back for the whole
                // handling of it, so the trail on the far side names the person rather than the
                // platform. Only for the journal and for context - never for permission.
                rabbit.UseConsumeFilter(typeof(ActorRestoreConsumeFilter<>), context);
                rabbit.UsePublishFilter(typeof(ActorPropagationPublishFilter<>), context);
                rabbit.UseSendFilter(typeof(ActorPropagationSendFilter<>), context);

                rabbit.UseMessageRetry(retry =>
                {
                    retry.Incremental(
                        retryLimit: rabbitSettings.RetryLimit,
                        initialInterval: TimeSpan.FromSeconds(rabbitSettings.RetryInitialIntervalSeconds),
                        intervalIncrement: TimeSpan.FromSeconds(rabbitSettings.RetryIntervalIncrementSeconds));
                });

                rabbit.ConfigureEndpoints(context);

                // Commands ship prefix-free (see PrefixedEventEndpointNameFormatter) so the
                // sender's address matches the receiver's queue regardless of which service
                // registered the consumer.
                RegisterCommandEndpointConventions(new KebabCaseEndpointNameFormatter(false));
            });
        });

        return services;
    }

    private static void RegisterCommandEndpointConventions(IEndpointNameFormatter formatter)
    {
        var mapMethod = typeof(EndpointConvention)
            .GetMethod(nameof(EndpointConvention.Map), 1, [typeof(Uri)])!;

        var messageMethod = typeof(IEndpointNameFormatter)
            .GetMethod(nameof(IEndpointNameFormatter.Message))!;

        // Scanned by the assembly that holds the messages, not the one that holds their interfaces:
        // those live in the domain, which knows nothing about transport, and scanning there finds
        // nothing at all. A command without a registered address fails at the moment it is sent -
        // which is how the welcome mail for a new organisation went missing.
        var commandTypes = typeof(ContractsMarker).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IBusCommand).IsAssignableFrom(t));

        foreach (var commandType in commandTypes)
        {
            var queueName = (string)messageMethod.MakeGenericMethod(commandType).Invoke(formatter, null)!;
            mapMethod.MakeGenericMethod(commandType).Invoke(null, [new Uri($"queue:{queueName}")]);
        }
    }

    /// <summary>
    /// Configures EntityFramework Transactional Outbox for the specified DbContext.
    /// Uses reflection because MassTransit requires a generic type parameter.
    /// </summary>
    private static void ConfigureOutbox(IBusRegistrationConfigurator bus, Type dbContextType)
    {
        typeof(DependencyInjection)
            .GetMethod(nameof(ConfigureOutboxGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(dbContextType)
            .Invoke(null, [bus]);
    }

    private static void ConfigureOutboxGeneric<TDbContext>(IBusRegistrationConfigurator bus)
        where TDbContext : DbContext
    {
        bus.AddEntityFrameworkOutbox<TDbContext>(outbox =>
        {
            outbox.UseDatabaseLocks();
            outbox.UseBusOutbox();
            outbox.DuplicateDetectionWindow = TimeSpan.FromSeconds(30);
        });
    }

    /// <summary>
    /// Validates that each IBusCommand has at most one consumer within the registered assemblies.
    /// Events are allowed multiple consumers (fan-out). Commands must be point-to-point.
    /// </summary>
    private static void ValidateCommandConsumers(Assembly[] assemblies)
    {
        var duplicates = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType
                            && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
                .Select(i => new
                {
                    ConsumerType = t,
                    MessageType = i.GetGenericArguments()[0]
                }))
            .Where(x => typeof(IBusCommand).IsAssignableFrom(x.MessageType))
            .GroupBy(x => x.MessageType)
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count == 0)
        {
            return;
        }

        var details = duplicates
            .Select(g =>
                $"  Command '{g.Key.Name}' has {g.Count()} consumers: " +
                string.Join(", ", g.Select(x => x.ConsumerType.Name)))
            .ToList();

        throw new InvalidOperationException(
            "Each IBusCommand must have exactly one consumer. Duplicates found:\n" +
            string.Join("\n", details) +
            "\n\nIf multiple services need to react to the same action, use IBusEvent instead.");
    }

    /// <summary>
    /// Validates that each IBusCommand consumer follows the '{CommandTypeName}Consumer' naming convention.
    /// MassTransit derives the receive endpoint name from the consumer class name (strips 'Consumer' suffix,
    /// applies kebab-case), while EndpointConvention derives the send address from the command type name
    /// (applies kebab-case directly). They must produce identical strings.
    /// </summary>
    private static void ValidateCommandConsumerNames(Assembly[] assemblies)
    {
        var violations = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
                .Select(i => new
                {
                    ConsumerType = t,
                    MessageType = i.GetGenericArguments()[0]
                }))
            .Where(x => typeof(IBusCommand).IsAssignableFrom(x.MessageType))
            .Where(x => x.ConsumerType.Name != x.MessageType.Name + "Consumer")
            .ToList();

        if (violations.Count == 0)
        {
            return;
        }

        var details = violations
            .Select(x =>
                $"  '{x.ConsumerType.Name}' consumes '{x.MessageType.Name}' — expected name: '{x.MessageType.Name}Consumer'")
            .ToList();

        throw new InvalidOperationException(
            "IBusCommand consumers must follow the '{CommandTypeName}Consumer' naming convention:\n" +
            string.Join("\n", details) +
            "\n\nThis ensures MassTransit endpoint routing (Consumer<T>) matches send routing (Message<T>).");
    }

}
