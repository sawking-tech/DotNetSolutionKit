using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Web.Tracing;

namespace NamespaceRoot.ProductName.Common.Tests.Tracing;

/// <summary>
/// The correlation identifier is what turns a pile of log lines from several services back into one story,
/// and what a client quotes when reporting a problem. Every path that produces it is checked here.
/// </summary>
[TestFixture]
public class CorrelationIdMiddlewareTests
{
    [Test]
    public async Task An_identifier_supplied_by_the_caller_is_kept_so_the_chain_stays_one_chain()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TracingHeaders.CorrelationId] = "gateway-abc-123";

        await Invoke(context);

        context.Items[TracingProperties.CorrelationId].ShouldBe("gateway-abc-123");
    }

    [Test]
    public async Task Without_a_supplied_identifier_the_trace_identifier_is_used()
    {
        using var activity = new Activity("request").Start();
        var context = new DefaultHttpContext();

        await Invoke(context);

        context.Items[TracingProperties.CorrelationId].ShouldBe(activity.TraceId.ToString());
    }

    [Test]
    public async Task Without_a_trace_an_identifier_is_still_produced()
    {
        Activity.Current = null;
        var context = new DefaultHttpContext();

        await Invoke(context);

        context.Items[TracingProperties.CorrelationId].ShouldBeOfType<string>()
            .ShouldNotBeNullOrWhiteSpace("work with no identifier cannot be found in the log");
    }

    [Test]
    public async Task The_identifier_comes_back_on_the_response()
    {
        var context = new DefaultHttpContext();
        var response = new CallbackRecordingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Request.Headers[TracingHeaders.CorrelationId] = "abc-123";

        await Invoke(context);
        await response.FireOnStartingAsync();

        context.Response.Headers[TracingHeaders.CorrelationId].ToString().ShouldBe("abc-123",
            "a client that reports a problem can only quote an identifier it was given");
    }

    [Test]
    public async Task Line_breaks_in_a_supplied_identifier_are_dropped()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TracingHeaders.CorrelationId] = "abc\r\nInjected: value";

        await Invoke(context);

        context.Items[TracingProperties.CorrelationId].ShouldBe("abcInjected: value",
            "the value reaches a log line and a response header, and must not be able to forge either");
    }

    [Test]
    public async Task An_oversized_identifier_is_truncated()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TracingHeaders.CorrelationId] = new string('x', 500);

        await Invoke(context);

        context.Items[TracingProperties.CorrelationId].ShouldBeOfType<string>().Length.ShouldBe(128,
            "the header comes from outside and must not be able to grow a log line without bound");
    }

    [Test]
    public async Task A_blank_supplied_identifier_is_treated_as_absent()
    {
        using var activity = new Activity("request").Start();
        var context = new DefaultHttpContext();
        context.Request.Headers[TracingHeaders.CorrelationId] = "   ";

        await Invoke(context);

        context.Items[TracingProperties.CorrelationId].ShouldBe(activity.TraceId.ToString());
    }

    [Test]
    public async Task The_identifier_travels_on_the_activity_so_outgoing_calls_can_carry_it()
    {
        using var activity = new Activity("request").Start();
        var context = new DefaultHttpContext();
        context.Request.Headers[TracingHeaders.CorrelationId] = "abc-123";

        await Invoke(context);

        activity.GetBaggageItem(TracingHeaders.CorrelationId).ShouldBe("abc-123");
    }

    private static Task Invoke(HttpContext context) =>
        new CorrelationIdMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

    /// <summary>
    /// The default response feature accepts <c>OnStarting</c> callbacks and never runs them, because
    /// nothing in a bare context ever starts a response. This one keeps them so the test can.
    /// </summary>
    private sealed class CallbackRecordingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public int StatusCode { get; set; } = 200;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) =>
            _onStarting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public async Task FireOnStartingAsync()
        {
            HasStarted = true;
            foreach (var (callback, state) in _onStarting)
            {
                await callback(state);
            }
        }
    }
}
