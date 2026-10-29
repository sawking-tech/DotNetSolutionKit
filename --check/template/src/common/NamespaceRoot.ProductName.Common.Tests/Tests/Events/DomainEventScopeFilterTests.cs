using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Events;

[TestFixture]
[TestOf(typeof(DomainEventScopeFilter<>))]
[Parallelizable(ParallelScope.All)]
public class DomainEventScopeFilterTests
{
    [Test]
    [Description("Filter must set DomainEventScopeContext.Current to the injected IServiceProvider during Send()")]
    public async Task Send_SetsScopeContext_DuringExecution()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var filter = new DomainEventScopeFilter<TestMessage>(sp);

        IServiceProvider? capturedScope = null;

        var pipe = new CallbackPipe<ConsumeContext<TestMessage>>(_ =>
        {
            capturedScope = DomainEventScopeContext.Current;
            return Task.CompletedTask;
        });

        await filter.Send(Mock.ConsumeContext<TestMessage>(), pipe);

        capturedScope.ShouldBe(sp);
    }

    [Test]
    [Description("Filter must clear DomainEventScopeContext.Current after Send() completes")]
    public async Task Send_ClearsScopeContext_AfterExecution()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var filter = new DomainEventScopeFilter<TestMessage>(sp);

        await filter.Send(Mock.ConsumeContext<TestMessage>(), new CallbackPipe<ConsumeContext<TestMessage>>(_ => Task.CompletedTask));

        DomainEventScopeContext.Current.ShouldBeNull();
    }

    [Test]
    [Description("Filter must clear scope even when the next pipe throws")]
    public async Task Send_ClearsScopeContext_WhenNextPipeThrows()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var filter = new DomainEventScopeFilter<TestMessage>(sp);

        var pipe = new CallbackPipe<ConsumeContext<TestMessage>>(_ => throw new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            filter.Send(Mock.ConsumeContext<TestMessage>(), pipe));

        DomainEventScopeContext.Current.ShouldBeNull();
    }

    // --- Helpers ---

    public sealed record TestMessage;

    private sealed class CallbackPipe<T>(Func<T, Task> callback) : IPipe<T> where T : class, PipeContext
    {
        public Task Send(T context) => callback(context);
        public void Probe(ProbeContext context) { }
    }

    private static class Mock
    {
        public static ConsumeContext<T> ConsumeContext<T>() where T : class
        {
            var mock = new Moq.Mock<ConsumeContext<T>>();
            return mock.Object;
        }
    }
}
