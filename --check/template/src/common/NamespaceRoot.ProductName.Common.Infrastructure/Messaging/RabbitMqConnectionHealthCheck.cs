using Microsoft.Extensions.Diagnostics.HealthChecks;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;
using RabbitMQ.Client;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

/// <summary>
/// Readiness of the broker itself: the service holds a connection to RabbitMQ, or opens one now.
/// </summary>
/// <remarks>
/// MassTransit's own check reports the receive endpoints. A service without consumers has none, so
/// that check stays healthy with the broker gone, while every publish waits for a connection - and
/// with the outbox, messages pile up in the database unseen. This check asks the broker directly.
/// The connection is kept between probes, so a healthy broker costs one open connection, not one per
/// probe.
/// </remarks>
internal sealed class RabbitMqConnectionHealthCheck(IConnectionFactory connectionFactory) : IHealthCheck, IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionHealthCheck(RabbitMqSettings settings)
        : this(new ConnectionFactory
        {
            HostName = settings.Host,
            UserName = settings.UserName,
            Password = settings.Password,
            RequestedConnectionTimeout = ConnectTimeout,
            ClientProvidedName = "readiness probe",
        })
    {
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
                return HealthCheckResult.Healthy();

            if (_connection is not null)
                await _connection.DisposeAsync();
            _connection = null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            _connection = await connectionFactory.CreateConnectionAsync(timeout.Token);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "RabbitMQ is unreachable", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
