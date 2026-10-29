using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Events;

[TestFixture]
[TestOf(typeof(DomainEventScopeContext))]
[Parallelizable(ParallelScope.All)]
public class DomainEventScopeContextTests
{
    [Test]
    [Description("Current should be null when no scope has been set")]
    public void Current_IsNull_ByDefault()
    {
        DomainEventScopeContext.Current.ShouldBeNull();
    }

    [Test]
    [Description("Use() should set Current to the provided provider for the duration of the scope")]
    public void Use_SetsCurrent_ForDurationOfScope()
    {
        var sp = BuildServiceProvider();

        using (DomainEventScopeContext.Use(sp))
        {
            DomainEventScopeContext.Current.ShouldBe(sp);
        }
    }

    [Test]
    [Description("Use() should restore null after the scope is disposed")]
    public void Use_RestoresNull_AfterDispose()
    {
        var sp = BuildServiceProvider();
        var scope = DomainEventScopeContext.Use(sp);

        scope.Dispose();

        DomainEventScopeContext.Current.ShouldBeNull();
    }

    [Test]
    [Description("Use() should restore the previous provider when scopes are nested")]
    public void Use_RestoresPreviousProvider_WhenNested()
    {
        var outer = BuildServiceProvider();
        var inner = BuildServiceProvider();

        using (DomainEventScopeContext.Use(outer))
        {
            using (DomainEventScopeContext.Use(inner))
            {
                DomainEventScopeContext.Current.ShouldBe(inner);
            }

            DomainEventScopeContext.Current.ShouldBe(outer);
        }

        DomainEventScopeContext.Current.ShouldBeNull();
    }

    [Test]
    [Description("AsyncLocal ensures each task has its own isolated scope - parallel tasks must not see each other's scope")]
    public async Task Current_IsIsolated_AcrossConcurrentTasks()
    {
        var sp1 = BuildServiceProvider();
        var sp2 = BuildServiceProvider();

        var barrier = new Barrier(2);
        IServiceProvider? seenInTask1 = null;
        IServiceProvider? seenInTask2 = null;

        var t1 = Task.Run(() =>
        {
            using (DomainEventScopeContext.Use(sp1))
            {
                barrier.SignalAndWait(); // sync: both tasks inside their scopes
                seenInTask1 = DomainEventScopeContext.Current;
            }
        });

        var t2 = Task.Run(() =>
        {
            using (DomainEventScopeContext.Use(sp2))
            {
                barrier.SignalAndWait();
                seenInTask2 = DomainEventScopeContext.Current;
            }
        });

        await Task.WhenAll(t1, t2);

        seenInTask1.ShouldBe(sp1);
        seenInTask2.ShouldBe(sp2);
    }

    private static IServiceProvider BuildServiceProvider() =>
        new ServiceCollection().BuildServiceProvider();
}
