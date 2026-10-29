using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

/// <summary>
/// Logs one CRITICAL line at host startup whenever <c>AddMessaging</c> fell back to the
/// no-op message bus (because <c>RabbitMq:Enabled</c> is false or the section is missing).
/// The no-op bus silently drops every Publish/Send call — without this announcer an
/// operator only finds out by noticing that downstream services are not receiving events,
/// which is exactly the symptom we just chased on dev.
/// </summary>
internal sealed class NoOpMessageBusStartupAnnouncer(
    ILogger<NoOpMessageBusStartupAnnouncer> logger,
    IHostEnvironment env)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogCritical(
            "MESSAGING DISABLED in {Environment}: NoOpMessageBus is wired — every bus.PublishAsync / bus.SendAsync will silently drop messages. " +
            "RabbitMq:Enabled is false or the RabbitMq configuration section is missing. " +
            "Set RabbitMq__Enabled=true (and RabbitMq__Host) to enable real delivery.",
            env.EnvironmentName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
