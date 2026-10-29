namespace NamespaceRoot.ProductName.Common.Attributes;

/// <summary>
/// Declares that a repository method writes without going through the change tracker —
/// <c>ExecuteUpdateAsync</c>, <c>ExecuteDeleteAsync</c> or raw SQL — and states whether the audit
/// journal is fed explicitly in its place.
/// </summary>
/// <remarks>
/// <para>
/// Set-based writes exist for good reasons: an atomic counter that must not lose a race, a subtree
/// update that would otherwise load thousands of rows. What they have in common is that the audit
/// interceptor cannot see them, so an entity marked <c>[Auditable]</c> quietly produces nothing for
/// exactly the operations that move counters, balances and access.
/// </para>
/// <para>
/// The marker does not change behaviour. It forces the author to answer the question at the moment
/// they choose to bypass the tracker, and lets a guard test fail on the next method that does not.
/// Set <see cref="Audited"/> to <c>true</c> when the caller records the change through
/// <c>IAuditRecorder</c>, and to <c>false</c> with a <see cref="Reason"/> when it deliberately does
/// not — a machine counter, a projection, session plumbing.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SetBasedWriteAttribute : Attribute
{
    /// <param name="audited">
    /// Whether the caller writes an audit entry for this change through <c>IAuditRecorder</c>.
    /// </param>
    /// <param name="reason">Why, in either case. Read by whoever asks where an entry went.</param>
    public SetBasedWriteAttribute(bool audited, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Audited = audited;
        Reason = reason;
    }

    /// <summary>Whether this write reaches the journal by an explicit record.</summary>
    public bool Audited { get; }

    /// <summary>Why it is recorded, or why it deliberately is not.</summary>
    public string Reason { get; }
}
