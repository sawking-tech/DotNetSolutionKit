using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;

/// <summary>Readiness of ClickHouse: a <c>SELECT 1</c> answers.</summary>
internal sealed class ClickHouseHealthCheck(IClickHouseConnections connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = connections.Create();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "ClickHouse is unreachable", ex);
        }
    }
}
