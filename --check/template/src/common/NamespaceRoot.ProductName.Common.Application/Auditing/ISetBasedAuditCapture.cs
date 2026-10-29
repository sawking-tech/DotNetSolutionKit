using System.Linq.Expressions;

namespace NamespaceRoot.ProductName.Common.Application.Auditing;

/// <summary>
/// Captures a write that bypasses the change tracker, by reading the affected rows either side of it.
/// </summary>
/// <remarks>
/// <para>
/// Use around <c>ExecuteUpdateAsync</c> / <c>ExecuteDeleteAsync</c> where the change carries an
/// operator decision. The interceptor cannot see those statements, so without this the journal is
/// silent about blocking a subtree of accounts, taking a slot of a limit or correcting a balance.
/// </para>
/// <para>
/// Both reads happen inside the caller's transaction — the previous values do not exist after it,
/// and a record written outside it could survive a rollback. Only the reads are on the critical
/// path; the diff and the per-row entries are produced downstream, in the consumer.
/// </para>
/// </remarks>
public interface ISetBasedAuditCapture
{
    /// <summary>
    /// Reads the rows matching <paramref name="affected"/> as they stand now, and returns a scope
    /// that reads them again on completion.
    /// </summary>
    /// <param name="affected">The rows the following statement is about to touch.</param>
    /// <param name="module">Business area the entries are filed under, as on <c>[Auditable]</c>.</param>
    /// <param name="subjectTenant">The tenant each row belongs to; entries without one are platform-only.</param>
    /// <param name="display">Human-readable identifier of a row, when it has one.</param>
    Task<ISetBasedAuditScope> BeginAsync<TEntity>(
        IQueryable<TEntity> affected,
        string module,
        Expression<Func<TEntity, Guid?>>? subjectTenant = null,
        Expression<Func<TEntity, string?>>? display = null,
        CancellationToken ct = default)
        where TEntity : class;
}

/// <summary>
/// The second half of a set-based capture: reads the rows again and publishes both snapshots.
/// </summary>
public interface ISetBasedAuditScope
{
    /// <summary>
    /// Reads the affected rows as they now stand and publishes the pair for downstream diffing.
    /// </summary>
    /// <remarks>
    /// Call inside the same transaction as the statement. Skipping the call — because the statement
    /// threw, or the operation was abandoned — publishes nothing, which is the correct outcome: the
    /// journal describes changes that landed.
    /// </remarks>
    Task CompleteAsync(string? reason = null, CancellationToken ct = default);
}
