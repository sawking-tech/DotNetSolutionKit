namespace NamespaceRoot.ProductName.Common.Application.Auditing;

/// <summary>
/// Records a change the persistence layer cannot see, so it reaches the journal anyway.
/// </summary>
/// <remarks>
/// <para>
/// Capture normally happens in the SaveChanges interceptor, which reads the change tracker. A write
/// issued as set-based SQL — <c>ExecuteUpdateAsync</c>, <c>ExecuteDeleteAsync</c>, raw commands —
/// never reaches the tracker, so no entry is produced however the entity is marked. A balance moved
/// with <c>ExecuteUpdateAsync</c>, to stay correct under concurrent writes, is the usual example.
/// </para>
/// <para>
/// This is deliberately not a general escape hatch. Use it where an operator makes a decision that
/// the tracker cannot observe: a manual balance correction, a transfer between accounts. Machine
/// traffic that happens to be set-based, such as a counter bumped per request, stays out: one entry per
/// event would bury the journal, and whatever records the traffic itself already answers for it.
/// </para>
/// <para>
/// The call joins the caller's transaction, exactly like interceptor-captured entries: the message
/// goes to the outbox through the same <c>DbContext</c>, so a rolled back correction leaves no
/// record of having happened.
/// </para>
/// </remarks>
public interface IAuditRecorder
{
    /// <summary>
    /// Records one change against an entity, filling actor, timestamp and trace from the ambient
    /// execution context.
    /// </summary>
    /// <param name="module">Business area the entry is filed under, as on <c>[Auditable]</c>.</param>
    /// <param name="entityType">Type name of the changed entity, as the interceptor would record it.</param>
    /// <param name="entityId">Identifier of the changed row.</param>
    /// <param name="entityDisplay">Human-readable identifier, when the entity has one.</param>
    /// <param name="action">Created / Updated / Deleted — see <c>AuditAction</c>.</param>
    /// <param name="changes">Properties that moved, as <c>name → (old, new)</c>.</param>
    /// <param name="subjectTenantId">The tenant the changed row belongs to. Without it the entry is platform-only.</param>
    /// <param name="reason">Operator-supplied justification, where the operation collects one.</param>
    Task RecordAsync(
        string module,
        string entityType,
        string entityId,
        string? entityDisplay,
        string action,
        IReadOnlyDictionary<string, (string? Old, string? New)> changes,
        Guid? subjectTenantId = null,
        string? reason = null,
        CancellationToken ct = default);
}
