using NamespaceRoot.ProductName.Common.Domain.Specifications;

namespace NamespaceRoot.ProductName.Common.Tests.Specifications;

/// <summary>
/// The specification is what a caller hands to a repository, so what it accepts and what it produces is
/// part of the contract rather than an implementation detail of the persistence layer.
/// </summary>
[TestFixture]
public class QuerySpecificationTests
{
    [Test]
    public void Without_a_criterion_the_query_selects_everything()
    {
        var query = new QuerySpecification<Plan>();

        var matches = query.ToExpression().Compile();

        matches(SamplePlan(isActive: true)).ShouldBeTrue();
        matches(SamplePlan(isActive: false)).ShouldBeTrue();
    }

    [Test]
    public void A_criterion_selects_only_what_it_describes()
    {
        var query = new QuerySpecification<Plan>(new ActivePlans());

        var matches = query.ToExpression().Compile();

        matches(SamplePlan(isActive: true)).ShouldBeTrue();
        matches(SamplePlan(isActive: false)).ShouldBeFalse("the criterion is the whole filter");
    }

    [Test]
    public void Narrowing_a_query_keeps_both_conditions()
    {
        var query = new QuerySpecification<Plan>(new ActivePlans()).And(new PlansUnder(1000));

        var matches = query.ToExpression().Compile();

        matches(SamplePlan(isActive: true, priceMinor: 500)).ShouldBeTrue();
        matches(SamplePlan(isActive: true, priceMinor: 5000)).ShouldBeFalse();
        matches(SamplePlan(isActive: false, priceMinor: 500)).ShouldBeFalse();
    }

    [Test]
    public void Narrowing_a_query_carries_the_relations_it_already_declared()
    {
        var query = new QuerySpecification<Plan>(new ActivePlans())
            .Include(plan => plan.Region)
            .And(new PlansUnder(1000));

        query.IncludePaths.ShouldBe(new[] { "Region" });
    }

    [Test]
    public void Narrowing_leaves_the_original_query_untouched()
    {
        var original = new QuerySpecification<Plan>(new ActivePlans());

        original.And(new PlansUnder(1000));

        original.ToExpression().Compile()(SamplePlan(isActive: true, priceMinor: 5000))
            .ShouldBeTrue("a narrowed copy must not change the query it came from");
    }

    [Test]
    public void A_relation_is_recorded_as_the_path_the_ORM_understands()
    {
        var query = new QuerySpecification<Plan>()
            .Include(plan => plan.Region)
            .Include(plan => plan.Region.Plans);

        query.IncludePaths.ShouldBe(new[] { "Region", "Region.Plans" });
    }

    [Test]
    public void An_include_that_names_no_relation_is_rejected_where_it_is_written()
    {
        var query = new QuerySpecification<Plan>();

        var include = () => query.Include(_ => "not a relation");

        Should.Throw<ArgumentException>(include, "a bad include should fail at the call site, not as an "
            + "unexplained empty relation later");
    }

    private static Plan SamplePlan(bool isActive = true, int priceMinor = 100)
    {
        var region = new Region(Guid.NewGuid(), "Europe");
        return new Plan(Guid.NewGuid(), "Sample", priceMinor, isActive, region);
    }
}
