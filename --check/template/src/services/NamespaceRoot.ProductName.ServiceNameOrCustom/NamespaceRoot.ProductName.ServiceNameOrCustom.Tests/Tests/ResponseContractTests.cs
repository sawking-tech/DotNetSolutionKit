using NamespaceRoot.ProductName.Common.Tests.Rules;
using NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Tests.Tests;

/// <summary>
/// What the service's controllers answer with.
/// </summary>
public class ResponseContractTests
{
    [Test]
    public void No_response_carries_a_token()
    {
        TokensInResponses.Find(typeof(SchemaHost).Assembly).ShouldBeEmpty(
            "a token in a response body is readable by any script on the page; issue it into HttpOnly cookies " +
            "with AuthCookieExtensions.IssueTokenCookies and answer login with the user, see " +
            "docs/architecture/authentication-and-permissions.md");
    }
}
