using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Moq;
using NamespaceRoot.ProductName.Common.Domain.Querying;
using NamespaceRoot.ProductName.Common.Web.Errors;
using NamespaceRoot.ProductName.Common.Web.Pagination;
using NamespaceRoot.ProductName.Common.Web.Setup;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// The bounds of page and page size: what is rejected, how the rejection reaches the client, and that
/// the OpenAPI document states the same bounds.
/// </summary>
[TestFixture]
internal sealed class PaginationContractTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(PaginationContractTests).Assembly);
        builder.Services.AddValidation(typeof(PaginationContractTests).Assembly);
        builder.Services.AddPlatformErrorHandling(builder.Environment);

        _app = builder.Build();
        _app.UsePlatformErrorHandling();
        _app.MapControllers();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [TestCase(0, 20, "Page")]
    [TestCase(-1, 20, "Page")]
    [TestCase(1, 0, "PageSize")]
    [TestCase(1, -1, "PageSize")]
    [TestCase(1, 1001, "PageSize")]
    public void Should_RejectInvalidPagination(int page, int pageSize, string property)
    {
        var errors = PaginationContract.Validate(new DefaultPagination(page, pageSize));

        errors.Select(error => error.PropertyName).ShouldContain(property);
    }

    [TestCase(1)]
    [TestCase(1000)]
    public void Should_AcceptValidDefaultPaginationLimit(int pageSize)
        => PaginationContract.Validate(new DefaultPagination(1, pageSize)).ShouldBeEmpty();

    [TestCase(200, false)]
    [TestCase(201, true)]
    public void Should_RespectRequestSpecificMaximum(int pageSize, bool shouldFail)
        => PaginationContract.Validate(new LimitedPagination(1, pageSize)).Any().ShouldBe(shouldFail);

    [Test(Description = "Out-of-bounds paging is a 422 validation problem, and the action never runs")]
    public async Task Should_Return422BeforeTheAction_When_PaginationIsInvalid()
    {
        var response = await _client.GetAsync("/paging-probe?page=0&pageSize=1001");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var fields = problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ToList();
        fields.ShouldBe(["page", "pageSize"], ignoreOrder: true);
        problem.GetProperty("code").GetString().ShouldBe("VALIDATION_ERROR");
        PagingProbeController.Calls.ShouldBe(0);
    }

    [Test(Description = "Paging within bounds reaches the action")]
    public async Task Should_ReachTheAction_When_PaginationIsValid()
    {
        var response = await _client.GetAsync("/paging-probe/limited?page=1&pageSize=200");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test(Description = "A request-specific limit is enforced over the default one")]
    public async Task Should_Return422_When_TheRequestSpecificLimitIsExceeded()
    {
        var response = await _client.GetAsync("/paging-probe/limited?page=1&pageSize=201");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Test(Description = "An action that opts out is not checked")]
    public async Task Should_SkipValidation_When_TheActionOptsOut()
    {
        var response = await _client.GetAsync("/paging-probe/not-paged?page=0&pageSize=0");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public void Should_PublishRequestSpecificBounds_InOpenApi()
    {
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter { Name = "page", Schema = new OpenApiSchema() },
                new OpenApiParameter { Name = "pageSize", Schema = new OpenApiSchema() },
            ],
        };
        var method = typeof(PagingProbeController).GetMethod(nameof(PagingProbeController.Limited))!;
        var context = new OperationFilterContext(
            new ApiDescription(),
            Mock.Of<ISchemaGenerator>(),
            new SchemaRepository(),
            method);

        new PaginationOperationFilter().Apply(operation, context);

        operation.Parameters.Single(parameter => parameter.Name == "page").Schema.Minimum.ShouldBe(1m);
        operation.Parameters.Single(parameter => parameter.Name == "pageSize").Schema.Minimum.ShouldBe(1m);
        operation.Parameters.Single(parameter => parameter.Name == "pageSize").Schema.Maximum.ShouldBe(200m);
    }

    private sealed record DefaultPagination(int Page, int PageSize) : IPaginationRequest;

    [PaginationLimit(200)]
    private sealed record LimitedPagination(int Page, int PageSize) : IPaginationRequest;
}

public sealed class PagingRequest : IPaginationRequest
{
    public int Page { get; init; }
    public int PageSize { get; init; }
}

[PaginationLimit(200)]
public sealed class LimitedPagingRequest : IPaginationRequest
{
    public int Page { get; init; }
    public int PageSize { get; init; }
}

[ApiController]
[Route("paging-probe")]
public sealed class PagingProbeController : ControllerBase
{
    public static int Calls;

    [HttpGet]
    public IActionResult Get([FromQuery] PagingRequest request)
    {
        Interlocked.Increment(ref Calls);
        return Ok();
    }

    [HttpGet("limited")]
    public IActionResult Limited([FromQuery] LimitedPagingRequest request) => Ok();

    [HttpGet("not-paged")]
    [SkipPaginationValidation]
    public IActionResult NotPaged([FromQuery] PagingRequest request) => Ok();
}
