using NamespaceRoot.ProductName.Common.Domain.Messaging;

namespace NamespaceRoot.ProductName.Common.Contracts.Messaging.Audit;

/// <summary>
/// Published for every change to an entity marked <c>[Auditable]</c>. Every service emits it; whichever
/// service keeps the journal consumes it.
/// </summary>
/// <remarks>
/// Written to the transactional outbox in the same transaction as the change it describes, so a rolled
/// back change leaves no journal entry.
/// </remarks>
public sealed record AuditRecordedV1 : IBusEvent
{
    public required Guid Id { get; init; }
    public required DateTimeOffset OccurredOnUtc { get; init; }

    /// <summary>Business module, from the entity's <c>[Auditable]</c>.</summary>
    public required string Module { get; init; }

    /// <summary>CLR type name of the changed entity.</summary>
    public required string EntityType { get; init; }

    /// <summary>Primary key of the changed row, as text: keys are not all Guids.</summary>
    public required string EntityId { get; init; }

    /// <summary>Human-readable identifier of the row; null when the entity declares none.</summary>
    public string? EntityDisplay { get; init; }

    /// <summary>One of <see cref="AuditAction"/>.</summary>
    public required string Action { get; init; }

    /// <summary>
    /// Changed properties as JSON: <c>{"Status":{"old":"Draft","new":"Placed"}}</c>. Empty for a deletion,
    /// where the action says everything.
    /// </summary>
    public required string Changes { get; init; }

    /// <summary>The operator's justification, where the operation asks for one.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Who made the change. <see cref="Guid.Empty"/> is the system: a background job, a consumer, a webhook.
    /// </summary>
    public required Guid ActorUserId { get; init; }

    /// <summary>Login of the acting user; null for the system.</summary>
    public string? ActorLogin { get; init; }

    /// <summary>The tenant the acting user acts for.</summary>
    public Guid? ActorTenantId { get; init; }

    /// <summary>
    /// The tenant the changed row belongs to; null when the entity has no owner or declares no subject.
    /// </summary>
    /// <remarks>
    /// What lets a change the system made be shown to the tenant it happened to: such a change has no
    /// acting tenant.
    /// </remarks>
    public Guid? SubjectTenantId { get; init; }

    /// <summary>The service that made the change: every service writes to one journal.</summary>
    public required string SourceService { get; init; }

    /// <summary>The trace of the request, job or message handling the change was made in.</summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// The correlation identifier of the external request behind the change, the one a caller searches
    /// the logs by.
    /// </summary>
    public string? CorrelationId { get; init; }
}
