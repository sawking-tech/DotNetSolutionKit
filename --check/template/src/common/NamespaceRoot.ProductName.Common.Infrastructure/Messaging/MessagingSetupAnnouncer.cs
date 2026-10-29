using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

/// <summary>
/// Prints one structured log line at host startup describing how the message-bus
/// infrastructure was wired (RabbitMQ host + whether transactional outbox is enabled
/// for this service). Gated by <c>Diagnostics:Messaging:LogStartup</c>; default off
/// so production logs stay quiet.
/// </summary>
internal sealed class MessagingSetupAnnouncer(
    ILogger<MessagingSetupAnnouncer> logger,
    IOptions<RabbitMqSettings> rabbitOptions,
    IHostEnvironment env,
    IConfiguration configuration,
    OutboxRegistrationState outboxState)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Diagnostics:Messaging:LogStartup"))
            return Task.CompletedTask;

        var rabbit = rabbitOptions.Value;
        logger.LogInformation(
            "MassTransit messaging wired in {Environment}: RabbitMqEnabled={Enabled}, Host={Host}, OutboxDbContext={OutboxDbContext}",
            env.EnvironmentName,
            rabbit.Enabled,
            rabbit.Host,
            outboxState.DbContextType?.Name ?? "(none)");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Singleton carrying the DbContext type that was passed to <c>AddMessaging&lt;TDbContext&gt;</c>.
/// Lets the announcer print the actual outbox-bound DbContext without taking a hard
/// dependency on EF Core types in the DI registration code.
/// </summary>
internal sealed class OutboxRegistrationState
{
    public Type? DbContextType { get; set; }
}
