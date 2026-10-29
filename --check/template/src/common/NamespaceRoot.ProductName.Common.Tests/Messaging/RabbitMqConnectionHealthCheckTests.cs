using Microsoft.Extensions.Diagnostics.HealthChecks;
using NamespaceRoot.ProductName.Common.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace NamespaceRoot.ProductName.Common.Tests.Messaging;

/// <summary>
/// The readiness check of the broker: a broker that cannot be reached fails readiness quickly, with
/// the reason attached, instead of the probe hanging or reporting healthy.
/// </summary>
[TestFixture]
internal class RabbitMqConnectionHealthCheckTests
{
    private static HealthCheckContext Context(HealthStatus failureStatus = HealthStatus.Unhealthy) => new()
    {
        Registration = new HealthCheckRegistration("rabbitmq", _ => null!, failureStatus, ["ready"]),
    };

    // Nothing listens on port 1, so the connection is refused at once.
    private static RabbitMqConnectionHealthCheck Unreachable() => new(new ConnectionFactory
    {
        HostName = "127.0.0.1",
        Port = 1,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(2),
    });

    [Test(Description = "An unreachable broker fails readiness and says why")]
    public async Task Should_BeUnhealthy_When_TheBrokerIsUnreachable()
    {
        await using var check = Unreachable();

        var result = await check.CheckHealthAsync(Context());

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("RabbitMQ is unreachable");
        result.Exception.ShouldNotBeNull();
    }

    [Test(Description = "The failure status of the registration is respected")]
    public async Task Should_ReportTheRegisteredFailureStatus()
    {
        await using var check = Unreachable();

        var result = await check.CheckHealthAsync(Context(HealthStatus.Degraded));

        result.Status.ShouldBe(HealthStatus.Degraded);
    }
}
