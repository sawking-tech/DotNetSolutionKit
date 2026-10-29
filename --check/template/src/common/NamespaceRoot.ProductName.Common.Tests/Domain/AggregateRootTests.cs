using NamespaceRoot.ProductName.Common.Domain;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Tests.Domain;

/// <summary>
/// Domain events are how a change tells the rest of the system what happened. The parts pinned here are
/// the ones a mistake in would be silent: an event raised and never collected, or collected twice.
/// </summary>
[TestFixture]
public class AggregateRootTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Renamed(IDomainExecutionContext Context, string Name) : IDomainEvent
    {
        public DateTimeOffset OccurredAt => Context.TimeProvider.GetUtcNow();
    }

    private sealed class Account : AggregateRoot<Guid>
    {
        public Account() => Id = Guid.NewGuid();

        public string Name { get; private set; } = string.Empty;

        public void Rename(IDomainExecutionContext context, string name)
        {
            Name = name;
            MarkUpdated(context);
            AddDomainEvent(new Renamed(context, name));
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Noon;
    }

    private sealed class TestContext : IDomainExecutionContext
    {
        public IUserContext Actor { get; } = null!;

        public TimeProvider TimeProvider { get; } = new TestClock();
    }

    [Test]
    public void A_change_raises_its_event()
    {
        var account = new Account();

        account.Rename(new TestContext(), "Acme");

        account.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<Renamed>()
            .Name.ShouldBe("Acme");
    }

    [Test]
    public void An_event_is_stamped_from_the_context_clock_rather_than_the_machine()
    {
        var account = new Account();

        account.Rename(new TestContext(), "Acme");

        account.DomainEvents.Single().OccurredAt.ShouldBe(Noon,
            "otherwise a test cannot place an event in time and handlers cannot trust the ordering");
    }

    [Test]
    public void Untouched_aggregates_raise_nothing()
    {
        new Account().DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Several_changes_keep_the_order_they_happened_in()
    {
        var account = new Account();
        var context = new TestContext();

        account.Rename(context, "First");
        account.Rename(context, "Second");

        account.DomainEvents.Cast<Renamed>().Select(e => e.Name).ShouldBe(new[] { "First", "Second" });
    }

    [Test]
    public void Collected_events_are_forgotten_so_they_are_not_dispatched_twice()
    {
        var account = new Account();
        account.Rename(new TestContext(), "Acme");

        ((IHasDomainEvents)account).ClearDomainEvents();

        account.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Clearing_is_out_of_reach_of_business_code()
    {
        typeof(Account).GetMethod("ClearDomainEvents").ShouldBeNull(
            "the method is implemented explicitly so only the persistence layer can call it");
    }

    [Test]
    public void An_aggregate_root_is_recognisable_as_one()
    {
        new Account().ShouldBeAssignableTo<IAggregateRoot>(
            "the shared repository stores aggregate roots and nothing else");
    }
}
