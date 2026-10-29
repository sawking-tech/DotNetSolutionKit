using System.Text;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Security;

/// <summary>
/// The Basic credentials check behind the background-jobs dashboard: the right pair passes, anything
/// else, malformed headers included, is refused without an exception.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class BasicCredentialsTests
{
    private const string User = "admin";
    private const string Password = "test-do-not-use-pass";

    private static string Basic(string pair) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(pair));

    [Test(Description = "The configured user and password pass")]
    public void Should_Match_When_BothAreRight() =>
        BasicCredentials.Match(Basic($"{User}:{Password}"), User, Password).ShouldBeTrue();

    [Test(Description = "A password may contain a colon; only the first one separates the user")]
    public void Should_Match_When_ThePasswordContainsAColon() =>
        BasicCredentials.Match(Basic("admin:a:b"), "admin", "a:b").ShouldBeTrue();

    [Test(Description = "The scheme name is case-insensitive")]
    public void Should_Match_When_TheSchemeIsLowercase() =>
        BasicCredentials.Match(Basic($"{User}:{Password}").Replace("Basic", "basic"), User, Password).ShouldBeTrue();

    [TestCase("admin:wrong", TestName = "A wrong password is refused")]
    [TestCase("other:test-do-not-use-pass", TestName = "A wrong user is refused")]
    [TestCase("admin:test-do-not-use-pas", TestName = "A password one character short is refused")]
    [TestCase("admin", TestName = "A pair without a colon is refused")]
    public void Should_Refuse_When_ThePairIsWrong(string pair) =>
        BasicCredentials.Match(Basic(pair), User, Password).ShouldBeFalse();

    [TestCase(null, TestName = "No header is refused")]
    [TestCase("", TestName = "An empty header is refused")]
    [TestCase("Bearer abc", TestName = "Another scheme is refused")]
    [TestCase("Basic", TestName = "A scheme without credentials is refused")]
    [TestCase("Basic not-base64!", TestName = "Credentials that are not base64 are refused")]
    public void Should_Refuse_When_TheHeaderIsMalformed(string? header) =>
        BasicCredentials.Match(header, User, Password).ShouldBeFalse();
}
