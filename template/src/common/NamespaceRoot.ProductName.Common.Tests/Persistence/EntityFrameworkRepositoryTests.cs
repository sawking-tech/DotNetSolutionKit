using NamespaceRoot.ProductName.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Domain.Specifications;
using NamespaceRoot.ProductName.Common.Domain.Querying;

// The assertion library ships a SortDirection of its own, and the global usings bring it into scope here.
using SortDirection = NamespaceRoot.ProductName.Common.Domain.Querying.SortDirection;

namespace NamespaceRoot.ProductName.Common.Tests.Persistence;

/// <summary>
/// The shared repository base is what every service inherits its reads from, so filtering, paging,
/// ordering and eager loading are pinned here once instead of in each service that derives from it.
/// </summary>
[TestFixture]
public class EntityFrameworkRepositoryTests
{
    private CatalogueDbContext _context = null!;
    private PlanRepository _plans = null!;
    private Region _europe = null!;
    private Region _asia = null!;

    [SetUp]
    public async Task SetUp()
    {
        _context = new CatalogueDbContext(new DbContextOptionsBuilder()
            .UseInMemoryDatabase($"catalogue-{TestContext.CurrentContext.Test.ID}")
            .Options);

        _europe = new Region(Guid.NewGuid(), "Europe");
        _asia = new Region(Guid.NewGuid(), "Asia");

        _context.AddRange(
            new Plan(Guid.NewGuid(), "Basic", 500, isActive: true, _europe),
            new Plan(Guid.NewGuid(), "Standard", 1500, isActive: true, _europe),
            new Plan(Guid.NewGuid(), "Premium", 4000, isActive: true, _europe),
            new Plan(Guid.NewGuid(), "Retired", 100, isActive: false, _europe),
            new Plan(Guid.NewGuid(), "Regional", 900, isActive: true, _asia));

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _plans = new PlanRepository(_context);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task A_query_returns_what_its_criterion_describes_and_nothing_else()
    {
        var active = await _plans.ListAsync(new QuerySpecification<Plan>(new ActivePlans()));

        active.Count().ShouldBe(4);
        active.ShouldAllBe(plan => plan.IsActive);
    }

    [Test]
    public async Task Conditions_compose_instead_of_growing_a_method_per_pair()
    {
        var query = new QuerySpecification<Plan>(new ActivePlans() & new PlansInRegion(_europe.Id));

        var found = await _plans.ListAsync(query);

        found.Select(plan => plan.Name).ShouldBe(new[] { "Basic", "Standard", "Premium" });
    }

    [Test]
    public async Task A_page_reports_the_size_of_the_whole_result_not_of_the_page()
    {
        var query = new QuerySpecification<Plan>(new ActivePlans());

        var page = await _plans.ListPageAsync(query, new PageRequest { Page = 1, PageSize = 2 });

        page.Items.Count().ShouldBe(2);
        page.TotalCount.ShouldBe(4, "paging must not hide how much there is");
        page.TotalPages.ShouldBe(2);
    }

    [Test]
    public async Task Consecutive_pages_do_not_repeat_or_skip_entities()
    {
        var query = new QuerySpecification<Plan>(new ActivePlans());

        var first = await _plans.ListPageAsync(query, new PageRequest { Page = 1, PageSize = 2 });
        var second = await _plans.ListPageAsync(query, new PageRequest { Page = 2, PageSize = 2 });

        var seen = first.Items.Concat(second.Items).Select(plan => plan.Id).ToList();
        seen.ShouldBeUnique();
        seen.Count().ShouldBe(4);
    }

    [Test]
    public async Task A_page_is_ordered_by_the_field_the_request_asks_for()
    {
        var query = new QuerySpecification<Plan>(new ActivePlans());

        var page = await _plans.ListPageAsync(
            query,
            new PageRequest { Page = 1, PageSize = 10, SortBy = "price", SortDir = SortDirection.Desc });

        page.Items.Select(plan => plan.Name).ShouldBe(new[] { "Premium", "Standard", "Regional", "Basic" });
    }

    [Test]
    public async Task A_field_the_repository_does_not_expose_is_refused_and_says_what_is_available()
    {
        var query = new QuerySpecification<Plan>(new ActivePlans());

        // Refused rather than quietly ordered by something else: a caller who mistyped a column
        // would otherwise see a list in an order they did not ask for and no way to learn why.
        var refusal = await Should.ThrowAsync<BadRequestException>(() => _plans.ListPageAsync(
            query,
            new PageRequest { Page = 1, PageSize = 10, SortBy = "isActive", SortDir = SortDirection.Desc }));

        refusal.ErrorCode.ShouldBe("INVALID_SORT_FIELD");
        refusal.Message.ShouldContain("name");
        refusal.Message.ShouldContain("price");
    }

    [Test]
    public async Task A_request_without_paging_numbers_still_reads_a_bounded_page()
    {
        var query = new QuerySpecification<Plan>();

        var page = await _plans.ListPageAsync(query, new PageRequest());

        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(PaginationRequestExtensions.DefaultPageSize);
        page.Items.Count().ShouldBe(5);
    }

    [Test]
    public async Task A_query_that_matches_nothing_returns_an_empty_page_rather_than_null()
    {
        var query = new QuerySpecification<Plan>(new PlansUnder(0));

        var page = await _plans.ListPageAsync(query, new PageRequest { Page = 3, PageSize = 25 });

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
        page.TotalPages.ShouldBe(0);
        page.Page.ShouldBe(3, "the request is echoed back even when it is past the end");
    }

    [Test]
    public async Task A_declared_relation_arrives_loaded()
    {
        var query = new QuerySpecification<Plan>(new PlansInRegion(_asia.Id)).Include(plan => plan.Region);

        var found = await _plans.SingleOrDefaultAsync(query);

        found.ShouldNotBeNull();
        found!.Region.ShouldNotBeNull();
        found.Region.Name.ShouldBe("Asia");
    }

    [Test]
    public async Task Counting_answers_from_the_criterion_alone()
    {
        var query = new QuerySpecification<Plan>(new ActivePlans()).Include(plan => plan.Region);

        var count = await _plans.CountAsync(query);
        var any = await _plans.AnyAsync(new QuerySpecification<Plan>(new PlansUnder(50)));

        count.ShouldBe(4);
        any.ShouldBeFalse();
    }

    [Test]
    public async Task An_aggregate_is_read_back_by_its_identifier()
    {
        var expected = await _plans.SingleOrDefaultAsync(
            new QuerySpecification<Plan>(new PlansInRegion(_asia.Id)));

        var found = await _plans.GetByIdAsync(expected!.Id);

        found.ShouldNotBeNull();
        found!.Name.ShouldBe("Regional");
    }

    [Test]
    public async Task An_added_aggregate_is_visible_once_the_unit_of_work_is_saved()
    {
        // The relation is read back inside this unit of work: attaching an entity the context does not
        // track would make it try to insert the region a second time.
        var asia = await _context.Regions.FindAsync(_asia.Id);

        _plans.Add(new Plan(Guid.NewGuid(), "Added", 700, isActive: true, asia!));
        await _context.SaveChangesAsync();

        var found = await _plans.AnyAsync(
            new QuerySpecification<Plan>(new PlansUnder(800) & new PlansInRegion(_asia.Id)));

        found.ShouldBeTrue();
    }
}
