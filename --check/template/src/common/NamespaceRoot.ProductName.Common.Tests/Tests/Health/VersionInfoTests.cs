using System.Text;
using NamespaceRoot.ProductName.Common.Contracts.Health;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Health;

[TestFixture]
public class VersionInfoTests
{
    [Test]
    public void LoadFromFile_NonexistentPath_ReturnsEmpty()
    {
        var result = VersionInfo.LoadFromFile(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));

        result.ShouldBe(VersionInfo.Empty);
        result.Version.ShouldBe("unknown");
        result.ReleaseNotes.ShouldBeNull();
        result.History.ShouldBeEmpty();
    }

    [Test]
    public void Parse_MissingVersionField_ReturnsEmpty()
    {
        var json = """
        {
          "releaseNotes": {
            "0.1.0": { "headline": "h", "highlights": ["a"] }
          }
        }
        """;

        var result = VersionInfo.Parse(ToStream(json));

        result.ShouldBe(VersionInfo.Empty);
    }

    [Test]
    public void Parse_MalformedJson_ReturnsEmpty()
    {
        var result = VersionInfo.Parse(ToStream("{ this is not json"));

        result.ShouldBe(VersionInfo.Empty);
    }

    [Test]
    public void Parse_ValidManifest_ExposesVersionAndCurrentEntry()
    {
        var json = """
        {
          "version": "0.7.0",
          "releaseNotes": {
            "0.7.0": { "headline": "Latest release", "highlights": ["a", "b"] },
            "0.1.0": { "headline": "First release", "highlights": ["init"] }
          }
        }
        """;

        var result = VersionInfo.Parse(ToStream(json));

        result.Version.ShouldBe("0.7.0");
        result.ReleaseNotes.ShouldNotBeNull();
        result.ReleaseNotes!.Headline.ShouldBe("Latest release");
        result.ReleaseNotes.Highlights.ShouldBe(new[] { "a", "b" });
    }

    [Test]
    public void Parse_HistorySortedDescendingByVersion()
    {
        var json = """
        {
          "version": "0.7.0",
          "releaseNotes": {
            "0.1.0": { "headline": "h1", "highlights": ["x"] },
            "0.7.0": { "headline": "h7", "highlights": ["x"] },
            "0.2.0": { "headline": "h2", "highlights": ["x"] },
            "0.10.0": { "headline": "h10", "highlights": ["x"] }
          }
        }
        """;

        var result = VersionInfo.Parse(ToStream(json));

        result.History.Select(h => h.Version)
            .ShouldBe(new[] { "0.10.0", "0.7.0", "0.2.0", "0.1.0" });
    }

    [Test]
    public void Parse_VersionWithNoMatchingNotes_CurrentIsNull_HistoryStillPopulated()
    {
        var json = """
        {
          "version": "9.9.9",
          "releaseNotes": {
            "0.1.0": { "headline": "h1", "highlights": ["x"] }
          }
        }
        """;

        var result = VersionInfo.Parse(ToStream(json));

        result.Version.ShouldBe("9.9.9");
        result.ReleaseNotes.ShouldBeNull();
        result.History.ShouldHaveSingleItem();
        result.History[0].Version.ShouldBe("0.1.0");
    }

    private static Stream ToStream(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));
}
