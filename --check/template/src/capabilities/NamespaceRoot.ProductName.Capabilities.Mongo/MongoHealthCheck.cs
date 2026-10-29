using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;

namespace NamespaceRoot.ProductName.Capabilities.Mongo;

/// <summary>Readiness of MongoDB: the service's database answers a <c>ping</c>.</summary>
internal sealed class MongoHealthCheck(IMongoStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await store.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "MongoDB is unreachable", ex);
        }
    }
}
