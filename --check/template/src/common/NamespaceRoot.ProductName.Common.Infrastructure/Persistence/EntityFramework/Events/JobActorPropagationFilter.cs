using System.Diagnostics;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Carries the operator who enqueued a job into the job itself, along with the trace and the
/// correlation identifier of the work that enqueued it.
/// </summary>
/// <remarks>
/// <para>
/// Internal endpoints trigger jobs on demand: an administrator closes a cycle, replays a delivery,
/// re-syncs a provider. The work then happens on a Hangfire thread with no HTTP context, and
/// everything it writes would be attributed to the system - "the platform closed this cycle", with
/// no way to tell it from the nightly run that does the same thing unattended.
/// </para>
/// <para>
/// Identity is captured at enqueue time, when the claims are still there, and restored around the
/// execution. Scheduled jobs carry no such parameter and stay system-attributed, which is the
/// truthful answer for them.
/// </para>
/// <para>
/// The trace and the correlation identifier travel for every job, scheduled or not: a job enqueued
/// by a request continues its trace and logs under its identifier, and one with nothing behind it
/// starts a trace of its own and is correlated by it.
/// </para>
/// </remarks>
public sealed class JobActorPropagationFilter(IServiceProvider services)
    : IClientFilter, IServerFilter
{
    private const string UserIdParameter = "ActorUserId";
    private const string LoginParameter = "ActorLogin";
    private const string TenantParameter = "ActorTenantId";
    private const string TraceParentParameter = "TraceParent";
    private const string CorrelationParameter = "CorrelationId";
    private const string RestoredItem = "JobContextRestored";

    private static readonly ActivitySource Jobs = new("NamespaceRoot.ProductName.Jobs");

    static JobActorPropagationFilter()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == Jobs.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
        });
    }

    public void OnCreating(CreatingContext context)
    {
        // The trace and the correlation identifier of whatever enqueued the job, so its lines are found
        // with the request's; written whoever enqueued it.
        if (Activity.Current?.Id is { } traceParent)
            context.SetJobParameter(TraceParentParameter, traceParent);
        if (Correlation.Current is { } correlationId)
            context.SetJobParameter(CorrelationParameter, correlationId);

        // Resolved per enqueue rather than injected: the filter is a singleton and the actor is not.
        using var scope = services.CreateScope();
        var actor = scope.ServiceProvider.GetService<IUserContext>();

        if (actor is null || IsAnonymous(actor)) return;

        context.SetJobParameter(UserIdParameter, actor.UserId);
        context.SetJobParameter(LoginParameter, actor.Login);
        context.SetJobParameter(TenantParameter, actor.TenantId);
    }

    public void OnCreated(CreatedContext context) { }

    public void OnPerforming(PerformingContext context)
    {
        var restored = new Stack<IDisposable>();

        var traceParent = context.GetJobParameter<string?>(TraceParentParameter);
        var activity = string.IsNullOrWhiteSpace(traceParent)
            ? Jobs.StartActivity("job", ActivityKind.Internal)
            : Jobs.StartActivity("job", ActivityKind.Internal, traceParent);
        if (activity is not null)
            restored.Push(activity);

        var correlationId = context.GetJobParameter<string?>(CorrelationParameter)
                            ?? Activity.Current?.TraceId.ToString()
                            ?? Guid.NewGuid().ToString("n");
        restored.Push(Correlation.Use(correlationId));

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<JobActorPropagationFilter>();
        if (logger?.BeginScope(new Dictionary<string, object?> { [TracingProperties.CorrelationId] = correlationId }) is { } logScope)
            restored.Push(logScope);

        var userId = context.GetJobParameter<Guid?>(UserIdParameter);
        if (userId is { } id && id != Guid.Empty)
        {
            var actor = new JobTriggeredByUserContext(
                id,
                context.GetJobParameter<string?>(LoginParameter),
                context.GetJobParameter<Guid?>(TenantParameter));
            restored.Push(JobActorContext.Use(actor));
        }

        context.Items[RestoredItem] = restored;
    }

    public void OnPerformed(PerformedContext context)
    {
        // Undone in reverse: the actor, the log scope, the correlation, then the activity.
        if (context.Items.TryGetValue(RestoredItem, out var item) && item is Stack<IDisposable> restored)
            while (restored.TryPop(out var disposable))
                disposable.Dispose();
    }

    /// <summary>
    /// Whether there is nobody to carry - a scheduled run, or an enqueue from another job.
    /// </summary>
    private static bool IsAnonymous(IUserContext actor)
    {
        try
        {
            return actor.IsSystemCall || actor.UserId == Guid.Empty;
        }
        catch
        {
            // No claims to read at all: enqueued from a job or at startup.
            return true;
        }
    }
}
