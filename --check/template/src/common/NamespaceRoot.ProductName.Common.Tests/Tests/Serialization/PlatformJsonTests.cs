using System.Text.Json;
using NamespaceRoot.ProductName.Common.Application.Serialization;
using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Serialization;

/// <summary>
/// The serialization settings are the wire format of every service, so they are checked against actual
/// output rather than by reading the configuration.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class PlatformJsonTests
{
    private static JsonSerializerOptions Options => PlatformJson.Options;

    private sealed record Sample(
        string CustomerName,
        AuthMethod Method,
        DateTimeOffset OccurredAt,
        string? Comment = null);

    private static string Write(Sample sample) => JsonSerializer.Serialize(sample, Options);

    private static Sample Read(string json) => JsonSerializer.Deserialize<Sample>(json, Options)!;

    [Test(Description = "An enum travels as its name")]
    public void Should_WriteAnEnumAsItsName() =>
        Write(new Sample("Acme", AuthMethod.ApiKey, DateTimeOffset.UnixEpoch)).ShouldContain("\"method\":\"ApiKey\"");

    [Test(Description = "An enum is read back from its name")]
    public void Should_ReadAnEnumFromItsName() =>
        Read("""{"customerName":"Acme","method":"Jwt","occurredAt":"2026-01-19T10:30:00Z"}""").Method.ShouldBe(AuthMethod.Jwt);

    [Test(Description = "Property names are camelCase")]
    public void Should_WriteCamelCaseNames()
    {
        var json = Write(new Sample("Acme", AuthMethod.Jwt, DateTimeOffset.UnixEpoch));

        json.ShouldContain("\"customerName\"");

        // Case.Sensitive is the point: the assertion library compares case-insensitively by default, and
        // "CustomerName" would match "customerName", passing while proving nothing.
        json.ShouldNotContain("\"CustomerName\"", Case.Sensitive);
    }

    [Test(Description = "A null is left out rather than written")]
    public void Should_LeaveOutNulls() =>
        Write(new Sample("Acme", AuthMethod.Jwt, DateTimeOffset.UnixEpoch)).ShouldNotContain("comment");

    [Test(Description = "A timestamp is written in UTC whatever offset it carried")]
    public void Should_WriteTimestampsInUtc()
    {
        var noonPlusThree = new DateTimeOffset(2026, 1, 19, 15, 30, 0, TimeSpan.FromHours(3));

        Write(new Sample("Acme", AuthMethod.Jwt, noonPlusThree)).ShouldContain("\"occurredAt\":\"2026-01-19T12:30:00Z\"");
    }

    [Test(Description = "A timestamp without sub-second precision does not gain any")]
    public void Should_NotInventFractionalSeconds()
    {
        var json = Write(new Sample("Acme", AuthMethod.Jwt, new DateTimeOffset(2026, 1, 19, 10, 0, 0, TimeSpan.Zero)));

        json.ShouldContain("\"occurredAt\":\"2026-01-19T10:00:00Z\"");
        json.ShouldNotContain(".000");
    }

    [Test(Description = "A timestamp read with an offset becomes UTC")]
    public void Should_ReadTimestampsIntoUtc()
    {
        var sample = Read("""{"customerName":"Acme","method":"Jwt","occurredAt":"2026-01-19T15:30:00+03:00"}""");

        sample.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        sample.OccurredAt.Hour.ShouldBe(12);
    }

    [Test(Description = "Non-ASCII text stays readable instead of turning into escapes")]
    public void Should_KeepNonAsciiReadable() =>
        Write(new Sample("Grüne Straße", AuthMethod.Jwt, DateTimeOffset.UnixEpoch)).ShouldContain("Grüne Straße");
}
