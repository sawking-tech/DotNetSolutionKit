using Hangfire;
using Hangfire.Client;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

/// <summary>
/// Registers Hangfire filters that need the built service provider.
/// </summary>
/// <remarks>
/// <see cref="JobActorPropagationFilter"/> resolves a scoped <c>IUserContext</c> per enqueue, so it
/// cannot be constructed while the container is still being described. Attaching it from a hosted
/// service runs after the provider exists and before any job is processed.
/// </remarks>
public sealed class HangfireFilterInstaller(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var filter = services.GetRequiredService<JobActorPropagationFilter>();

        GlobalJobFilters.Filters.Add(filter);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
