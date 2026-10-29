using Microsoft.AspNetCore.Mvc;
using NamespaceRoot.ProductName.Common.Web.Swagger;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// Which Swagger document a route lands in: its version's, or only "all" when it has none.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class ApiVersionHelperTests
{
    [TestCase("api/v1/orders", "v1", TestName = "A route attribute without a leading slash")]
    [TestCase("/api/v2/orders/{id}", "v2", TestName = "A document path with a leading slash")]
    [TestCase("api/v10/orders", "v10", TestName = "A two-digit version")]
    [TestCase("api/v3", "v3", TestName = "A version at the end of the route")]
    public void Should_ExtractTheVersion(string route, string expected) =>
        ApiVersionHelper.ExtractVersionFromRoute(route).ShouldBe(expected);

    [TestCase("", TestName = "An empty route has no version")]
    [TestCase("/health", TestName = "An unversioned route has no version")]
    [TestCase("internal/jobs", TestName = "An internal route has no version")]
    [TestCase("api/v1beta/orders", TestName = "A segment that only starts like a version is not one")]
    [TestCase("myapi/v1/orders", TestName = "A segment that only ends like api is not one")]
    public void Should_FindNoVersion(string route) =>
        ApiVersionHelper.ExtractVersionFromRoute(route).ShouldBeNull();

    [Test(Description = "Versions come in numeric order, and unversioned controllers add none")]
    public void Should_DiscoverVersionsInNumericOrder() =>
        ApiVersionHelper.DiscoverAllVersions(typeof(ApiVersionHelperTests).Assembly)
            .Where(v => v is "v1" or "v2" or "v10")
            .ShouldBe(["v1", "v2", "v10"]);

    [Route("api/v10/version-probe")]
    private sealed class V10Controller : ControllerBase;

    [Route("api/v2/version-probe")]
    private sealed class V2Controller : ControllerBase;

    [Route("api/v1/version-probe")]
    private sealed class V1Controller : ControllerBase;

    [Route("version-probe")]
    private sealed class UnversionedController : ControllerBase;
}
