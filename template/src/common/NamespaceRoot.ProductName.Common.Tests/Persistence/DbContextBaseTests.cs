using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Domain.Events;
using NamespaceRoot.ProductName.Common.Tests.Stubs;

namespace NamespaceRoot.ProductName.Common.Tests.Persistence;

/// <summary>
/// The transaction and change-tracking behaviour every service context inherits.
/// </summary>
/// <remarks>
/// Both cases here are about what happens on the failure path, which is where a context is least
/// often exercised and most often wrong: services all follow the same shape of committing inside a
/// try and rolling back from the catch, so a context that mishandles either loses the caller's
/// original error in every one of them at once.
/// </remarks>
[TestFixture]
public class DbContextBaseTests
{
    private CatalogueDbContext _context = null!;

    [SetUp]
    public void SetUp() =>
        _context = new CatalogueDbContext(new DbContextOptionsBuilder()
            .UseInMemoryDatabase($"catalogue-txn-{TestContext.CurrentContext.Test.ID}")
            .Options);

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public void Rolling_back_with_nothing_to_roll_back_is_not_an_error()
    {
        // The shape every service writes: commit in the try, roll back from the catch. A commit that
        // failed has already released its transaction, so by the time the catch runs there is nothing
        // left to undo. Complaining about that here would replace the failure being handled with a
        // complaint about the handling, and the caller would never learn why the write actually failed.
        Should.NotThrow(async () => await _context.RollbackTransactionAsync());
    }

    [Test]
    public async Task Discarding_changes_drops_the_events_raised_while_making_them()
    {
        var region = new Region(Guid.NewGuid(), "Europe");
        var plan = new Plan(Guid.NewGuid(), "Basic", 500, isActive: true, region);
        _context.Add(plan);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var tracked = await _context.Plans.SingleAsync(p => p.Id == plan.Id);
        tracked.Reprice(900, new TestDomainExecutionContext(
            UserContextMockFactory.CreateSystemUser(), TimeProvider.System));

        _context.DiscardChanges();

        // Left in place, the event would still be waiting on the entity, and the next save through this
        // context would publish a repricing that was never written.
        ((IHasDomainEvents)tracked).DomainEvents.ShouldBeEmpty();
        _context.Entry(tracked).State.ShouldBe(EntityState.Unchanged);
    }

    [Test]
    public async Task Discarding_changes_forgets_rows_that_were_never_written()
    {
        var region = new Region(Guid.NewGuid(), "Asia");
        var plan = new Plan(Guid.NewGuid(), "Regional", 900, isActive: true, region);
        _context.Add(plan);

        _context.DiscardChanges();

        _context.Entry(plan).State.ShouldBe(EntityState.Detached);
        (await _context.Plans.CountAsync()).ShouldBe(0);
    }
}
