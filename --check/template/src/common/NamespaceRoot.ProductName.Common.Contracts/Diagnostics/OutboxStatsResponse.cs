namespace NamespaceRoot.ProductName.Common.Contracts.Diagnostics;

/// <summary>
/// Snapshot of a service's MassTransit EF Outbox table for live debugging. Returned by the
/// per-service <c>/api/v1/diagnostics/outbox-stats</c> endpoint so operators can confirm
/// whether outbound bus messages are being persisted by the SaveChanges interceptor and
/// drained by the <c>BusOutboxDeliveryService</c> background worker.
/// </summary>
public sealed class OutboxStatsResponse
{
    /// <summary>Rows where <c>sent_time IS NULL</c> — pending delivery.</summary>
    public long Pending { get; set; }

    /// <summary>Rows where <c>sent_time IS NOT NULL</c> — already delivered.</summary>
    public long Sent { get; set; }

    /// <summary>Enqueue timestamp of the oldest pending row, if any. Helps spot a stuck worker.</summary>
    public DateTime? OldestPendingAt { get; set; }

    /// <summary>Enqueue timestamp of the most recently delivered row.</summary>
    public DateTime? LastSentAt { get; set; }

    /// <summary>
    /// Recent-rows sample, optionally filtered by the <c>filter</c> query parameter
    /// (body substring match — typically an entity id).
    /// </summary>
    public IReadOnlyList<OutboxRowSnapshot> Sample { get; set; } = [];
}

/// <summary>Minimal projection of one <c>outbox_message</c> row for the diagnostic endpoint.</summary>
public sealed class OutboxRowSnapshot
{
    public string MessageId { get; set; } = string.Empty;
    public DateTime? EnqueueTime { get; set; }
    public DateTime? SentTime { get; set; }
}
