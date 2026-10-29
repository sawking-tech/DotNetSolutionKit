using NamespaceRoot.ProductName.Common.Web.Setup;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// Which origins the platform CORS policy lets through.
/// </summary>
[TestFixture]
internal class PlatformCorsTests
{
    [TestCase("https://app.example.com", new[] { "https://app.example.com" }, true, TestName = "An exact origin matches")]
    [TestCase("https://APP.example.com", new[] { "https://app.example.com" }, true, TestName = "Case does not matter")]
    [TestCase("https://other.example.com", new[] { "https://app.example.com" }, false, TestName = "Another host does not match")]
    [TestCase("https://anything.test", new[] { "*" }, true, TestName = "A star allows any origin")]
    [TestCase("https://localhost:5173", new[] { "https://localhost:*" }, true, TestName = "host:* allows any port of the host")]
    [TestCase("http://localhost:5173", new[] { "https://localhost:*" }, false, TestName = "host:* keeps the scheme")]
    [TestCase("https://localhost.evil.test", new[] { "https://localhost:*" }, false, TestName = "host:* does not match a longer host")]
    [TestCase("", new[] { "*" }, false, TestName = "An empty origin never matches")]
    public void Matches(string origin, string[] allowed, bool expected) =>
        PlatformCors.IsOriginAllowed(origin, allowed).ShouldBe(expected);

    [Test]
    public void No_configured_origins_allow_nothing() =>
        PlatformCors.IsOriginAllowed("https://app.example.com", null).ShouldBeFalse();
}
