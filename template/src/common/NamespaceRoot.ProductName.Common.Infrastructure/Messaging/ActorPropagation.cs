using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

/// <summary>
/// Names the message carries the acting person under.
/// </summary>
/// <remarks>
/// The same three names the background-job filter uses for its parameters, so one convention covers
/// both ways work leaves a request: a job put on the scheduler and a message put on the bus.
/// </remarks>
public static class ActorHeaders
{
    public const string UserId = "ActorUserId";
    public const string Login = "ActorLogin";
    public const string TenantId = "ActorTenantId";
}

/// <summary>
/// Puts the acting person on every message leaving this service.
/// </summary>
/// <remarks>
/// A message is published because somebody asked for something: a customer placed an order, an
/// operator cancelled one. Without this the other side has nobody to name,
/// and everything it writes down reads as though the platform did it to itself.
/// <para>
/// A system call, or a flow with no person behind it at all, carries nothing: the receiving side
/// then falls back to the system, which is who acted.
/// </para>
/// </remarks>
public sealed class ActorPropagationPublishFilter<T>(IServiceProvider services) : IFilter<PublishContext<T>>
    where T : class
{
    public Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        ActorHeaderWriter.Write(services, context);
        return next.Send(context);
    }

    public void Probe(ProbeContext context) { }
}

/// <summary>The same for messages sent to one address rather than published.</summary>
public sealed class ActorPropagationSendFilter<T>(IServiceProvider services) : IFilter<SendContext<T>>
    where T : class
{
    public Task Send(SendContext<T> context, IPipe<SendContext<T>> next)
    {
        ActorHeaderWriter.Write(services, context);
        return next.Send(context);
    }

    public void Probe(ProbeContext context) { }
}

internal static class ActorHeaderWriter
{
    public static void Write(IServiceProvider services, SendContext context)
    {
        // The trace the sender is on, so the far side continues the same one instead of starting
        // its own. Written whoever the sender is: a job with no person behind it still belongs to
        // the run that produced it.
        if (Activity.Current?.Id is { } traceParent)
            context.Headers.Set("traceparent", traceParent);

        // The correlation identifier too: the caller may have chosen its own, which the trace does not carry.
        if (Correlation.Current is { } correlationId)
            context.Headers.Set(TracingHeaders.CorrelationId, correlationId);

        using var scope = services.CreateScope();
        var actor = scope.ServiceProvider.GetService<IUserContext>();

        if (actor is null || IsAnonymous(actor)) return;

        context.Headers.Set(ActorHeaders.UserId, actor.UserId.ToString());
        context.Headers.Set(ActorHeaders.Login, actor.Login);
        context.Headers.Set(ActorHeaders.TenantId, actor.TenantId?.ToString());
    }

    private static bool IsAnonymous(IUserContext actor)
    {
        try
        {
            return actor.IsSystemCall || actor.UserId == Guid.Empty;
        }
        catch
        {
            // No claims to read at all: published from a job, a consumer or at startup.
            return true;
        }
    }
}

/// <summary>
/// Restores the acting person for the whole handling of a message.
/// </summary>
/// <remarks>
/// The consumer runs outside any request, so without this everything underneath it — the audit
/// journal above all — sees the system and writes the system down. Reading the names off the
/// message and putting them back means the trail names who asked, and says it in the same
/// words the original request did.
/// <para>
/// What arrives is what the sending service claimed. Inside the platform that is fair enough for a
/// journal, and it is <b>only</b> for the journal and for context: nothing may be permitted on the
/// strength of it, or a forged message would become a way in.
/// </para>
/// </remarks>
public sealed class ActorRestoreConsumeFilter<T>(ILoggerFactory loggerFactory) : IFilter<ConsumeContext<T>>
    where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        using var activity = MessageTracing.Start(context);
        var actor = ReadActor(context);
        var correlationId = context.Headers.Get<string>(TracingHeaders.CorrelationId)
                            ?? Activity.Current?.TraceId.ToString()
                            ?? Guid.NewGuid().ToString("n");
        using var correlation = Correlation.Use(correlationId);

        // Every line the handler writes carries the identifier and the person without the handler
        // saying so — the same thing the request pipeline does for an HTTP call.
        using var logScope = loggerFactory
            .CreateLogger<ActorRestoreConsumeFilter<T>>()
            .BeginScope(new Dictionary<string, object?>
            {
                [TracingProperties.CorrelationId] = correlationId,
                ["ActorLogin"] = actor?.Login,
                ["ActorUserId"] = actor?.UserId,
            });

        if (actor is null)
        {
            await next.Send(context);
            return;
        }

        using (JobActorContext.Use(actor))
            await next.Send(context);
    }

    public void Probe(ProbeContext context) { }

    private static IUserContext? ReadActor(ConsumeContext context)
    {
        var raw = context.Headers.Get<string>(ActorHeaders.UserId);

        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty) return null;

        var tenant = context.Headers.Get<string>(ActorHeaders.TenantId);

        return new JobTriggeredByUserContext(
            userId,
            context.Headers.Get<string>(ActorHeaders.Login),
            Guid.TryParse(tenant, out var tenantId) ? tenantId : null);
    }
}

/// <summary>
/// Gives the handling of a message a trace of its own, continuing the one the sender was on.
/// </summary>
/// <remarks>
/// A request carries a trace because the web layer starts one; a consumer runs outside any request
/// and had none, so everything it wrote to the audit journal carried no trace at all and could not
/// be tied back to the operation that caused it.
/// <para>
/// The listener samples and keeps every activity and exports nothing: its only job is that the
/// identifiers exist while the message is being handled. Whoever wants
/// them shipped can add a collector on top without touching this.
/// </para>
/// </remarks>
internal static class MessageTracing
{
    private const string Name = "NamespaceRoot.ProductName.Messaging";

    private static readonly ActivitySource Source = new(Name);

    static MessageTracing()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        });
    }

    public static Activity? Start(ConsumeContext context)
    {
        // Already inside one: a consumer that publishes, and whatever it publishes is consumed in turn.
        if (Activity.Current is not null) return null;

        var parent = context.Headers.Get<string>("traceparent");

        return string.IsNullOrWhiteSpace(parent)
            ? Source.StartActivity("consume", ActivityKind.Consumer)
            : Source.StartActivity("consume", ActivityKind.Consumer, parent);
    }
}
