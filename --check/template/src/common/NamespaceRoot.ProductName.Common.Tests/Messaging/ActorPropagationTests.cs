using NamespaceRoot.ProductName.Common.Application.Tracing;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Messaging;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;
using NamespaceRoot.ProductName.Common.Tests.Stubs;

namespace NamespaceRoot.ProductName.Common.Tests.Messaging;

/// <summary>
/// Work that crosses the bus is still somebody's work. Without the acting person travelling with the
/// message, everything the receiving service writes to the audit journal reads as though the platform
/// did it to itself, and the trail stops at the service boundary — exactly where an investigation needs
/// it to continue.
/// </summary>
[TestFixture]
public class ActorPropagationTests
{
    private const string UserId = "11111111-1111-1111-1111-111111111111";
    private const string TenantId = "22222222-2222-2222-2222-222222222222";

    [SetUp]
    public void Reset() => Activity.Current = null;

    [Test]
    public async Task The_person_who_asked_travels_with_the_message()
    {
        var headers = await Publish(new UserContextMock(UserId, login: "operator@example.com"));

        headers[ActorHeaders.UserId].ShouldBe(UserId);
        headers[ActorHeaders.Login].ShouldBe("operator@example.com");
    }

    [Test]
    public async Task A_tenant_user_carries_the_tenant_they_act_for()
    {
        var actor = new UserContextMock(UserId, login: "user@example.com") { TenantId = Guid.Parse(TenantId) };

        var headers = await Publish(actor);

        headers[ActorHeaders.TenantId].ShouldBe(TenantId);
    }

    [Test]
    public async Task A_system_call_names_nobody()
    {
        var headers = await Publish(SystemUserContext.Instance);

        headers.ShouldNotContainKey(ActorHeaders.UserId,
            "naming a person where there is none would put a stranger's name on the platform's own work");
    }

    [Test]
    public async Task The_sender_trace_travels_so_the_far_side_continues_it()
    {
        using var activity = new Activity("request").Start();

        var headers = await Publish(new UserContextMock(UserId));

        headers["traceparent"].ShouldBe(activity.Id);
    }

    [Test]
    public async Task The_correlation_the_caller_chose_travels_with_the_message()
    {
        using var _ = Correlation.Use("client-chosen-42");

        var headers = await Publish(new UserContextMock(UserId));

        headers[TracingHeaders.CorrelationId].ShouldBe("client-chosen-42");
    }

    [Test]
    public async Task The_correlation_is_restored_for_the_whole_handling_of_the_message()
    {
        string? seen = null;

        await Consume(
            new Dictionary<string, object?> { [TracingHeaders.CorrelationId] = "client-chosen-42" },
            onHandling: () => seen = Correlation.Current);

        seen.ShouldBe("client-chosen-42", "the caller searching by its own identifier finds the consumer's lines too");
    }

    [Test]
    public async Task A_message_without_a_correlation_is_correlated_by_its_trace()
    {
        string? seen = null;

        await Consume(new Dictionary<string, object?>(), onHandling: () => seen = Correlation.Current);

        seen.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task The_person_is_restored_for_the_whole_handling_of_the_message()
    {
        IUserContext? seen = null;

        await Consume(
            new Dictionary<string, object?>
            {
                [ActorHeaders.UserId] = UserId,
                [ActorHeaders.Login] = "operator@example.com",
                [ActorHeaders.TenantId] = TenantId,
            },
            onHandling: () => seen = JobActorContext.Actor);

        seen.ShouldNotBeNull("without it the audit journal writes the system down instead of the person");
        seen!.UserId.ShouldBe(Guid.Parse(UserId));
        seen.Login.ShouldBe("operator@example.com");
        seen.TenantId.ShouldBe(Guid.Parse(TenantId));
    }

    [Test]
    public async Task Nothing_is_restored_when_the_message_names_nobody()
    {
        IUserContext? seen = null;

        await Consume(new Dictionary<string, object?>(), onHandling: () => seen = JobActorContext.Actor);

        seen.ShouldBeNull("a message with no person behind it is the platform's own work, and says so");
    }

    [Test]
    public async Task A_restored_person_is_named_but_brings_no_rights_with_them()
    {
        IUserContext? seen = null;

        await Consume(
            new Dictionary<string, object?> { [ActorHeaders.UserId] = UserId },
            onHandling: () => seen = JobActorContext.Actor);

        seen.ShouldNotBeNull();
        seen!.ApiKeyId.ShouldBeNull();
        seen.IsSystemCall.ShouldBeFalse();
        seen.ShouldBeOfType<JobTriggeredByUserContext>(
            "what a message claims about its sender is fine for naming them in the journal; the context " +
            "restored from it carries no rights of its own, so nothing can be permitted on that claim");
    }

    [Test]
    public async Task The_handling_of_a_message_continues_the_trace_it_came_from()
    {
        using var sender = new Activity("request").Start();
        string? traceInsideHandling = null;

        await Consume(
            new Dictionary<string, object?> { ["traceparent"] = sender.Id },
            onHandling: () => traceInsideHandling = Activity.Current?.TraceId.ToString(),
            outsideAnyActivity: true);

        traceInsideHandling.ShouldBe(sender.TraceId.ToString(),
            "one operation of one person has to read as one story across the services it touches");
    }

    [Test]
    public async Task Handling_gets_a_trace_even_when_the_message_carries_none()
    {
        string? traceInsideHandling = null;

        await Consume(
            new Dictionary<string, object?>(),
            onHandling: () => traceInsideHandling = Activity.Current?.TraceId.ToString(),
            outsideAnyActivity: true);

        traceInsideHandling.ShouldNotBeNullOrWhiteSpace(
            "work with no trace at all cannot be found in the journal afterwards");
    }

    // --- plumbing -----------------------------------------------------------------------------

    private static async Task<Dictionary<string, object?>> Publish(IUserContext actor)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => actor);

        var written = new Dictionary<string, object?>();
        var context = new Mock<PublishContext<TestMessage>>();
        context.SetupGet(c => c.Headers).Returns(new RecordingHeaders(written));

        var filter = new ActorPropagationPublishFilter<TestMessage>(services.BuildServiceProvider());
        await filter.Send(context.Object, new NoopPipe<PublishContext<TestMessage>>());

        return written;
    }

    private static async Task Consume(
        Dictionary<string, object?> headers,
        Action onHandling,
        bool outsideAnyActivity = false)
    {
        var previous = Activity.Current;
        if (outsideAnyActivity) Activity.Current = null;

        try
        {
            var context = new Mock<ConsumeContext<TestMessage>>();
            context.SetupGet(c => c.Headers).Returns(new RecordingHeaders(headers));

            var filter = new ActorRestoreConsumeFilter<TestMessage>(NullLoggerFactory.Instance);
            await filter.Send(context.Object, new NoopPipe<ConsumeContext<TestMessage>>(onHandling));
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    public sealed record TestMessage(string Value);

    /// <summary>Headers a test can both write into and read back, standing in for the transport's own.</summary>
    private sealed class RecordingHeaders(Dictionary<string, object?> values) : SendHeaders
    {
        public void Set(string key, string? value) => values[key] = value;

        public void Set(string key, string? value, bool overwrite = true) => values[key] = value;

        public void Set(string key, object? value, bool overwrite = true) => values[key] = value;

        public bool TryGetHeader(string key, [NotNullWhen(true)] out object? value) =>
            values.TryGetValue(key, out value) && value is not null;

        public IEnumerable<KeyValuePair<string, object>> GetAll() =>
            values.Where(p => p.Value is not null)
                  .Select(p => new KeyValuePair<string, object>(p.Key, p.Value!));

        public T? Get<T>(string key, T? defaultValue = default) where T : class =>
            values.TryGetValue(key, out var value) ? value as T ?? defaultValue : defaultValue;

        public T? Get<T>(string key, T? defaultValue = default) where T : struct =>
            values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;

        public IEnumerator<HeaderValue> GetEnumerator() =>
            values.Where(p => p.Value is not null)
                  .Select(p => new HeaderValue(p.Key, p.Value!))
                  .GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class NoopPipe<T>(Action? onSend = null) : IPipe<T> where T : class, PipeContext
    {
        public Task Send(T context)
        {
            onSend?.Invoke();
            return Task.CompletedTask;
        }

        public void Probe(ProbeContext context) { }
    }
}
