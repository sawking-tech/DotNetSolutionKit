using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

/// <summary>
/// Diagnostic announcer: builds the configured <see cref="DbContextOptions{TContext}"/>
/// at host startup and prints the list of EF interceptors that were ultimately attached.
/// MassTransit's EF Outbox relies on a SaveChangesInterceptor auto-wired through
/// <c>IConfigureOptions&lt;DbContextOptions&lt;TContext&gt;&gt;</c>; if the auto-wire silently
/// failed (as it appears to on dev), the MT interceptor will be absent from this list,
/// which immediately confirms the root cause of the outbox publish drop.
/// Gated by <c>Diagnostics:Messaging:LogStartup</c>; default off.
/// </summary>
internal sealed class OutboxStartupAnnouncer<TDbContext>(
    ILogger<OutboxStartupAnnouncer<TDbContext>> logger,
    IServiceProvider rootProvider,
    IConfiguration configuration)
    : IHostedService
    where TDbContext : DbContext
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Diagnostics:Messaging:LogStartup"))
            return Task.CompletedTask;

        try
        {
            using var scope = rootProvider.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<TDbContext>>();
            var coreExt = options.FindExtension<CoreOptionsExtension>();
            var names = coreExt?.Interceptors?.Select(i => i.GetType().FullName ?? i.GetType().Name).ToArray()
                        ?? Array.Empty<string>();
            logger.LogInformation(
                "DbContext {DbContext} resolved with {Count} interceptors attached: {Interceptors}",
                typeof(TDbContext).Name,
                names.Length,
                names);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to inspect interceptors on {DbContext} during startup",
                typeof(TDbContext).Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
