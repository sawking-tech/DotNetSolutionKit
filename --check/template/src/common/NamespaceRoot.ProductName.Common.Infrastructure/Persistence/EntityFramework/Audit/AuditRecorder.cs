using System.Diagnostics;
using System.Text.Json;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Application.Auditing;
using NamespaceRoot.ProductName.Common.Application.Messaging;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Contracts.Messaging.Audit;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;

/// <inheritdoc />
/// <remarks>
/// Fills the same fields the interceptor does, from the same sources, so an explicitly recorded
/// entry is indistinguishable from a captured one once it is in the journal — the reader should not
/// have to know which writes happened to go through the change tracker.
/// </remarks>
internal sealed class AuditRecorder(
    IDomainExecutionContext execution,
    IMessageBus bus,
    ISourceServiceName sourceService) : IAuditRecorder
{
    private static readonly JsonSerializerOptions ChangesSerializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Task RecordAsync(
        string module,
        string entityType,
        string entityId,
        string? entityDisplay,
        string action,
        IReadOnlyDictionary<string, (string? Old, string? New)> changes,
        Guid? subjectTenantId = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        var (actorUserId, actorLogin, actorTenantId) = AuditActorReader.Read(execution);

        var payload = changes.ToDictionary(
            pair => pair.Key,
            pair => new { old = pair.Value.Old, @new = pair.Value.New });

        return bus.PublishAsync(
            new AuditRecordedV1
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = execution.TimeProvider.GetUtcNow(),
                Module = module,
                EntityType = entityType,
                EntityId = entityId,
                EntityDisplay = entityDisplay,
                Action = action,
                Changes = JsonSerializer.Serialize(payload, ChangesSerializerOptions),
                Reason = reason,
                ActorUserId = actorUserId,
                ActorLogin = actorLogin,
                ActorTenantId = actorTenantId,
                SubjectTenantId = subjectTenantId,
                SourceService = sourceService.Value,
                TraceId = Activity.Current?.TraceId.ToString(),
                CorrelationId = Correlation.Current,
            },
            ct);
    }
}

/// <summary>
/// The name this service files its journal entries under.
/// </summary>
/// <remarks>
/// The interceptor reads it off the DbContext's default schema; an explicit recorder has no context
/// to hand, so the value is registered once per service instead of being guessed at each call site.
/// </remarks>
public interface ISourceServiceName
{
    string Value { get; }
}

/// <inheritdoc />
internal sealed class SourceServiceName(string value) : ISourceServiceName
{
    public string Value { get; } = value;
}
