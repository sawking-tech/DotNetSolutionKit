using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Web.Setup;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Validation;

/// <summary>
/// What AddValidation promises a controller: its arguments are validated before the action runs.
/// </summary>
[TestFixture]
internal class ValidationSetupTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(ValidationSetupTests).Assembly);
        builder.Services.AddValidation(typeof(ValidationSetupTests).Assembly);

        _app = builder.Build();
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

    [Test(Description = "An invalid body is rejected with 422 and the failing field, before the action runs")]
    public async Task Should_Return422_When_TheBodyFailsItsValidator()
    {
        var response = await _client.PostAsJsonAsync("/validation-probe", new ProbeRequest { Name = "", Tags = ["a"] });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Keys.ShouldContain(key => key.Equals(nameof(ProbeRequest.Name), StringComparison.OrdinalIgnoreCase));
    }

    [Test(Description = "A valid body reaches the action")]
    public async Task Should_ReachTheAction_When_TheBodyIsValid()
    {
        var response = await _client.PostAsJsonAsync("/validation-probe", new ProbeRequest { Name = "ok", Tags = ["a"] });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test(Description = "A rule chain stops at its first failure, so a null rejected by NotNull is not dereferenced by Must")]
    public async Task Should_Return422_When_ANullFailsTheFirstRuleOfAChain()
    {
        var response = await _client.PostAsJsonAsync("/validation-probe", new ProbeRequest { Name = "ok", Tags = null });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}

public sealed class ProbeRequest
{
    public string? Name { get; init; }
    public List<string>? Tags { get; init; }
}

public sealed class ProbeRequestValidator : AbstractValidator<ProbeRequest>
{
    public ProbeRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Tags).NotNull().Must(tags => tags!.Count > 0);
    }
}

[ApiController]
[Route("validation-probe")]
public sealed class ValidationProbeController : ControllerBase
{
    [HttpPost]
    public IActionResult Post(ProbeRequest request) => Ok();
}
