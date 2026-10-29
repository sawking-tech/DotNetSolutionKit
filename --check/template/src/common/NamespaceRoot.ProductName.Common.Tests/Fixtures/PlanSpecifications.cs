using System.Linq.Expressions;
using LinqSpecs;

namespace NamespaceRoot.ProductName.Common.Tests.Fixtures;

/// <summary>
/// Conditions written the way a service is expected to write them: one class per condition, combined with
/// the operators rather than by adding a repository method per pair.
/// </summary>
public sealed class ActivePlans : Specification<Plan>
{
    public override Expression<Func<Plan, bool>> ToExpression() => plan => plan.IsActive;
}

public sealed class PlansInRegion : Specification<Plan>
{
    private readonly Guid _regionId;

    public PlansInRegion(Guid regionId) => _regionId = regionId;

    public override Expression<Func<Plan, bool>> ToExpression() => plan => plan.RegionId == _regionId;
}

public sealed class PlansUnder : Specification<Plan>
{
    private readonly int _priceMinor;

    public PlansUnder(int priceMinor) => _priceMinor = priceMinor;

    public override Expression<Func<Plan, bool>> ToExpression() => plan => plan.PriceMinor < _priceMinor;
}
