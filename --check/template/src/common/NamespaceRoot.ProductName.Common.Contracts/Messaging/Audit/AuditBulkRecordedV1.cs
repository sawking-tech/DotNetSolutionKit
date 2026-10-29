using NamespaceRoot.ProductName.Common.Domain.Messaging;

namespace NamespaceRoot.ProductName.Common.Contracts.Messaging.Audit;

/// <summary>
/// One set-based write, carried as the rows it touched before and after — the journal entries are
/// derived from it downstream.
/// </summary>
/// <remarks>
/// <para>
/// A bulk write is invisible to the audit interceptor: <c>ExecuteUpdateAsync</c> never reaches the
/// change tracker. Blocking a subtree of accounts, taking a slot of a limit or moving a balance go this
/// way, and each of them is a decision somebody has to answer for.
/// </para>
/// <para>
/// The originating transaction stays cheap on purpose: it reads the affected columns twice — once
/// before the statement, once after — and publishes this single message. Turning the two snapshots
/// into per-row diffs and journal entries happens in the consumer, off the critical path, because
/// the operator is waiting on the block to apply, not on its paperwork.
/// </para>
/// <para>
/// The snapshots must be taken inside the writing transaction. Afterwards the previous values are
/// gone, and reading them post-commit would also cost the property this journal is built on: the
/// record shares the fate of the change it describes.
/// </para>
/// </remarks>
public sealed record AuditBulkRecordedV1 : IBusEvent
{
    public required Guid Id { get; init; }

    public required DateTimeOffset OccurredOnUtc { get; init; }

    public required string Module { get; init; }

    public required string EntityType { get; init; }

    /// <summary>
    /// Rows as they were, keyed by entity id. Absent from <see cref="After"/> means the row was
    /// deleted.
    /// </summary>
    public required IReadOnlyList<AuditRowSnapshot> Before { get; init; }

    /// <summary>
    /// Rows as they became. Absent from <see cref="Before"/> means the row appeared — either created
    /// by this statement, or inserted between the two reads.
    /// </summary>
    public required IReadOnlyList<AuditRowSnapshot> After { get; init; }

    /// <summary>
    /// Set when the write touched more rows than the snapshot limit, in which case the snapshots are
    /// empty and one summary entry is written instead of one per row.
    /// </summary>
    public int? TruncatedRowCount { get; init; }

    public string? Reason { get; init; }

    public required Guid ActorUserId { get; init; }

    public string? ActorLogin { get; init; }

    public Guid? ActorTenantId { get; init; }

    public required string SourceService { get; init; }

    public string? TraceId { get; init; }

    /// <summary>The correlation identifier of the external request behind the write.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>One row of a set-based write, as its audited columns stood at a point in time.</summary>
public sealed record AuditRowSnapshot
{
    /// <summary>Primary key, rendered as text — keys are not uniformly Guid across services.</summary>
    public required string EntityId { get; init; }

    /// <summary>Human-readable identifier of the row, when the entity declares one.</summary>
    public string? EntityDisplay { get; init; }

    /// <summary>The tenant the row belongs to, so the entry can be shown to that tenant.</summary>
    public Guid? SubjectTenantId { get; init; }

    /// <summary>Audited columns and their values at this point.</summary>
    public required IReadOnlyDictionary<string, string?> Values { get; init; }
}
