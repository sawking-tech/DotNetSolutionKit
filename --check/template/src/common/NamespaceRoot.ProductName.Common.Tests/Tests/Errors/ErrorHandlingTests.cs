using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Tests.Tests.Validation;
using NamespaceRoot.ProductName.Common.Web.Errors;
using NamespaceRoot.ProductName.Common.Web.Setup;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Errors;

/// <summary>
/// What a client receives for each kind of failure, end to end through the pipeline a service builds:
/// one RFC 9457 shape with the code and the correlation identifier, whatever produced the error.
/// </summary>
[TestFixture]
internal class ErrorHandlingTests
{
    private const string ProblemJson = "application/problem+json";

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(ErrorHandlingTests).Assembly);
        builder.Services.AddValidation(typeof(ErrorHandlingTests).Assembly);
        builder.Services.AddPlatformErrorHandling(builder.Environment);

        _app = builder.Build();
        _app.UsePlatformTracing();
        _app.UsePlatformErrorHandling();
        _app.MapControllers();
        _app.MapGet("/throw/not-found", IResult () => throw new NotFoundException("Order 42 not found"));
        _app.MapGet("/throw/rate-limit", IResult () =>
            throw new RateLimitException("orders", 10, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30)));
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Test(Description = "An exception becomes a problem with status, code, correlation and trace identifiers")]
    public async Task Should_WriteAProblem_When_AnExceptionEscapes()
    {
        var response = await _client.GetAsync("/throw/not-found");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemJson);
        var problem = await ReadAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe(404);
        problem.GetProperty("detail").GetString().ShouldBe("Order 42 not found");
        problem.GetProperty("code").GetString().ShouldBe("NOT_FOUND");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
        problem.GetProperty("correlationId").GetString()
            .ShouldBe(response.Headers.GetValues(TracingHeaders.CorrelationId).Single(),
                "the body and the header name the same request");
    }

    [Test(Description = "The identifier a caller sends comes back in the problem")]
    public async Task Should_EchoTheCallersCorrelationId_When_OneIsSent()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/throw/not-found");
        request.Headers.Add(TracingHeaders.CorrelationId, "caller-chosen-id");

        var response = await _client.SendAsync(request);

        (await ReadAsync(response)).GetProperty("correlationId").GetString().ShouldBe("caller-chosen-id");
    }

    [Test(Description = "A route that does not exist answers 404 as a problem, not with an empty body")]
    public async Task Should_WriteAProblem_When_NoRouteMatches()
    {
        var response = await _client.GetAsync("/nowhere");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemJson);
        var problem = await ReadAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe(404);
        problem.GetProperty("correlationId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test(Description = "A body failing its validator is a validation problem naming fields as the JSON does")]
    public async Task Should_WriteA422ValidationProblemWithJsonFieldNames_When_TheBodyIsInvalid()
    {
        var response = await _client.PostAsJsonAsync("/validation-probe", new ProbeRequest { Name = "", Tags = ["a"] });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemJson);
        var problem = await ReadAsync(response);
        var fields = problem.GetProperty("errors").EnumerateObject().Select(field => field.Name).ToList();
        fields.ShouldContain("name", "the request body says \"name\", so the error has to as well");
        fields.ShouldNotContain("Name");
        problem.GetProperty("correlationId").GetString().ShouldNotBeNullOrEmpty();
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test(Description = "A rate limit with a known delay tells the client when to retry")]
    public async Task Should_SetRetryAfter_When_ARateLimitCarriesADelay()
    {
        var response = await _client.GetAsync("/throw/rate-limit");

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(30));
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("RATE_LIMIT");
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
