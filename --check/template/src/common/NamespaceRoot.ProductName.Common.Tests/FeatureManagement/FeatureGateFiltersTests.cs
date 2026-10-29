using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;
using Microsoft.OpenApi.Models;
using Moq;
using NamespaceRoot.ProductName.Common.Application.FeatureManagement;
using NamespaceRoot.ProductName.Common.Web.FeatureManagement;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Tests.FeatureManagement;

/// <summary>
/// An action behind a closed <c>[FeatureGate]</c> is left out of the Swagger document, as it is left out of
/// the API; an open one stays.
/// </summary>
[TestFixture]
[TestOf(typeof(FeatureGateFilters))]
public class FeatureGateFiltersTests
{
    public sealed class GatedController : ControllerBase
    {
        [FeatureGate("orders.v2")]
        public void One() { }

        [FeatureGate(RequirementType.Any, "orders.v2", "orders.beta")]
        public void Either() { }

        [FeatureGate(negate: true, "orders.v2")]
        public void UntilV2() { }

        public void Ungated() { }
    }

    private static OpenApiDocument Document(TestFeatureCatalog catalog, params string[] actions)
    {
        var filters = new FeatureGateFilters(new ServiceCollection().AddSingleton<IFeatureCatalog>(catalog).BuildServiceProvider());
        var document = new OpenApiDocument { Paths = new OpenApiPaths() };
        foreach (var action in actions)
        {
            var operation = new OpenApiOperation();
            filters.Apply(operation, new OperationFilterContext(
                new ApiDescription(), Mock.Of<ISchemaGenerator>(), new SchemaRepository(),
                typeof(GatedController).GetMethod(action)!));
            document.Paths["/" + action] = new OpenApiPathItem
            {
                Operations = new Dictionary<OperationType, OpenApiOperation> { [OperationType.Get] = operation },
            };
        }

        filters.Apply(document, new DocumentFilterContext([], Mock.Of<ISchemaGenerator>(), new SchemaRepository()));
        return document;
    }

    [Test(Description = "A gate whose flag is off removes the operation, and the path with it")]
    public void Should_LeaveTheActionOut_When_ItsFlagIsOff()
    {
        var document = Document(new TestFeatureCatalog().With("orders.v2", false), "One", "Ungated");

        document.Paths.Keys.ShouldBe(["/Ungated"]);
    }

    [Test(Description = "A gate whose flag is on keeps the operation")]
    public void Should_KeepTheAction_When_ItsFlagIsOn()
    {
        var document = Document(new TestFeatureCatalog().With("orders.v2"), "One");

        document.Paths.Keys.ShouldBe(["/One"]);
    }

    [Test(Description = "A gate on any of two flags is open when one of them is on")]
    public void Should_KeepTheAction_When_AnyOfItsFlagsIsOn()
    {
        var document = Document(new TestFeatureCatalog().With("orders.v2", false).With("orders.beta"), "Either");

        document.Paths.Keys.ShouldBe(["/Either"]);
    }

    [Test(Description = "A negated gate closes when its flag turns on")]
    public void Should_LeaveTheActionOut_When_ANegatedFlagIsOn()
    {
        var document = Document(new TestFeatureCatalog().With("orders.v2"), "UntilV2");

        document.Paths.ShouldBeEmpty();
    }
}
