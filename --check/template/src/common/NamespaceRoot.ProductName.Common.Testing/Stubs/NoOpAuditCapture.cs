using System.Linq.Expressions;
using NamespaceRoot.ProductName.Common.Application.Auditing;

namespace NamespaceRoot.ProductName.Common.Tests.Stubs;

/// <summary>
/// Records nothing, for suites whose subject is not the journal.
/// </summary>
/// <remarks>
/// Set-based writes reach for <see cref="IAuditRecorder"/> from inside repositories, so without a
/// registration every fixture touching one of those repositories fails to build its service — for a
/// reason that has nothing to do with what it is testing. Suites that assert on the journal register
/// a recording double over this one.
/// </remarks>
public sealed class NoOpAuditRecorder : IAuditRecorder
{
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
        => Task.CompletedTask;
}

/// <inheritdoc cref="NoOpAuditRecorder"/>
public sealed class NoOpSetBasedAuditCapture : ISetBasedAuditCapture
{
    public Task<ISetBasedAuditScope> BeginAsync<TEntity>(
        IQueryable<TEntity> affected,
        string module,
        Expression<Func<TEntity, Guid?>>? subjectTenant = null,
        Expression<Func<TEntity, string?>>? display = null,
        CancellationToken ct = default)
        where TEntity : class
        => Task.FromResult<ISetBasedAuditScope>(new NoOpScope());

    private sealed class NoOpScope : ISetBasedAuditScope
    {
        public Task CompleteAsync(string? reason = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
