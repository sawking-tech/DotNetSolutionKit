using DotsKit.Generation;

namespace DotsKit.Tests;

/// <summary>The Messaging value in the words of each version: since 3.0 the value names the transport too.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class TemplateVersionsTests
{
    [TestCase("2.9.1", false)]
    [TestCase("3.0.0", true)]
    [TestCase("3.0.0-rc", true)]
    [TestCase("source", true)]
    public void NamesTransport_TellsTheVersionsThatNameTheTransport(string version, bool names) =>
        TemplateVersions.NamesTransport(version).ShouldBe(names);

    [TestCase("2.9.1", "outbox", null)]
    [TestCase("2.9.1", "rabbitmq:outbox", null)]
    [TestCase("3.0.0", "outbox", "rabbitmq:outbox")]
    [TestCase("3.0.0", "direct", "rabbitmq:direct")]
    [TestCase("3.0.0", "rabbitmq:outbox", null)]
    [TestCase("source", "direct", "rabbitmq:direct")]
    public void WhyNotATransport_RefusesTheDeliveryOfAnOlderRelease(string version, string value, string? transport)
    {
        var why = TemplateVersions.WhyNotATransport(value, version);

        if (transport is null)
        {
            why.ShouldBeNull();
        }
        else
        {
            why.ShouldNotBeNull().ShouldContain($"--Messaging {transport}");
        }
    }

    [TestCase("2.9.1", "outbox", "outbox")]
    [TestCase("2.9.1", "rabbitmq:direct", "direct")]
    [TestCase("3.0.0", "outbox", "rabbitmq:outbox")]
    [TestCase("3.0.0", "rabbitmq:direct", "rabbitmq:direct")]
    [TestCase("source", "outbox", "rabbitmq:outbox")]
    public void TransportValueFor_SpeaksTheWordsOfTheVersion(string version, string value, string expected) =>
        TemplateVersions.TransportValueFor(version, value).ShouldBe(expected);
}
