using Microsoft.AspNetCore.Mvc;

namespace NamespaceRoot.ProductName.Common.Tests.Rules;

/// <summary>
/// The rule a generated service runs over its controllers: it has to catch a token in a response, however
/// deep, and leave alone the tokens that are not credentials.
/// </summary>
[TestFixture]
internal class TokensInResponsesTests
{
    private sealed record Session(string AccessToken, string RefreshToken);

    private sealed record LoginResponse(string Name, Session Session);

    private sealed record Page(IReadOnlyList<string> Items, string? ContinuationToken);

    private sealed class AuthController : ControllerBase
    {
        public Task<ActionResult<LoginResponse>> Login() => throw new NotImplementedException();
    }

    private sealed class OrdersController : ControllerBase
    {
        public Task<Page> List() => throw new NotImplementedException();

        public IActionResult Delete() => throw new NotImplementedException();
    }

    [Test]
    public void A_token_in_a_response_is_caught_however_deep()
    {
        var findings = TokensInResponses.Find([typeof(AuthController)]);

        findings.ShouldBe(["AuthController.Login: Session.AccessToken", "AuthController.Login: Session.RefreshToken"],
            ignoreOrder: true);
    }

    [Test]
    public void A_continuation_token_and_a_bodiless_result_pass()
    {
        TokensInResponses.Find([typeof(OrdersController)]).ShouldBeEmpty();
    }

    [Test]
    public void The_platform_controllers_return_no_token()
    {
        TokensInResponses.Find(typeof(Web.Setup.PlatformWebHost).Assembly).ShouldBeEmpty();
    }
}
