// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Diagnostics;
using System.Linq.Expressions;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Application.Auditing;
using NamespaceRoot.ProductName.Common.Application.Messaging;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Contracts.Messaging.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;

/// <inheritdoc />
internal sealed class SetBasedAuditCapture(
    DbContextBase context,
    IDomainExecutionContext execution,
    IMessageBus bus,
    ISourceServiceName sourceService) : ISetBasedAuditCapture
{
    /// <summary>
    /// Above this many affected rows the snapshots are dropped and one summary entry is written.
    /// </summary>
    /// <remarks>
    /// A bulk statement is allowed to be bulk. Carrying a hundred thousand rows through a message
    /// to describe one operator click trades a fast write for a slow everything-else, which is the
    /// opposite of why the statement was written set-based.
    /// </remarks>
    private const int MaxSnapshotRows = 5_000;

    public async Task<ISetBasedAuditScope> BeginAsync<TEntity>(
        IQueryable<TEntity> affected,
        string module,
        Expression<Func<TEntity, Guid?>>? subjectTenant = null,
        Expression<Func<TEntity, string?>>? display = null,
        CancellationToken ct = default)
        where TEntity : class
    {
        var reader = new SnapshotReader<TEntity>(
            context, affected, subjectTenant?.Compile(), display?.Compile());

        var count = await affected.CountAsync(ct);
        var before = count > MaxSnapshotRows ? [] : await reader.ReadAsync(ct);

        return new Scope<TEntity>(
            this, reader, module, before, count > MaxSnapshotRows ? count : null);
    }

    private Task PublishAsync(
        string module,
        string entityType,
        IReadOnlyList<AuditRowSnapshot> before,
        IReadOnlyList<AuditRowSnapshot> after,
        int? truncatedRowCount,
        string? reason,
        CancellationToken ct)
    {
        var (actorUserId, actorLogin, actorTenantId) = AuditActorReader.Read(execution);

        return bus.PublishAsync(
            new AuditBulkRecordedV1
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = execution.TimeProvider.GetUtcNow(),
                Module = module,
                EntityType = entityType,
                Before = before,
                After = after,
                TruncatedRowCount = truncatedRowCount,
                Reason = reason,
                ActorUserId = actorUserId,
                ActorLogin = actorLogin,
                ActorTenantId = actorTenantId,
                SourceService = sourceService.Value,
                TraceId = Activity.Current?.TraceId.ToString(),
                CorrelationId = Correlation.Current,
            },
            ct);
    }

    /// <summary>Reads the audited columns of a query's rows, using EF's own model metadata.</summary>
    /// <remarks>
    /// Values are pulled through <see cref="IProperty"/> getters rather than by reflecting over the
    /// CLR type, so the column set matches what the interceptor records — shadow properties
    /// included, ignored infrastructure columns excluded — and a diff produced here is comparable
    /// with one produced there.
    /// </remarks>
    private sealed class SnapshotReader<TEntity>(
        DbContextBase context,
        IQueryable<TEntity> query,
        Func<TEntity, Guid?>? subjectTenant,
        Func<TEntity, string?>? display)
        where TEntity : class
    {
        private readonly IEntityType _entityType =
            context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped.");

        public string EntityTypeName => typeof(TEntity).Name;

        public async Task<IReadOnlyList<AuditRowSnapshot>> ReadAsync(CancellationToken ct)
        {
            var rows = await query.AsNoTracking().ToListAsync(ct);
            var key = _entityType.FindPrimaryKey();

            var properties = _entityType.GetProperties()
                .Where(p => !p.IsShadowProperty())
                .Where(p => !AuditColumnPolicy.IsAlwaysIgnored(p.Name) && !p.IsConcurrencyToken)
                .Where(p => key is null || key.Properties.All(k => k.Name != p.Name))
                .ToList();

            // Resolved once per read rather than per row: the attribute is a property of the type.
            var redacted = AuditRedaction.ResolveFor(typeof(TEntity));

            return rows.Select(row => new AuditRowSnapshot
            {
                EntityId = BuildKey(key, row),
                EntityDisplay = display?.Invoke(row),
                SubjectTenantId = subjectTenant?.Invoke(row),
                Values = properties.ToDictionary(
                    property => property.Name,
                    property => ReadValue(property, row, redacted)),
            }).ToList();
        }

        /// <summary>
        /// A property's recorded value: the value itself, or a fingerprint of it when the property
        /// is redacted.
        /// </summary>
        /// <remarks>
        /// Fingerprinted rather than blanked so the diff still shows that a secret moved. Blanking
        /// both snapshots would make every rotation compare equal and vanish from the journal — the
        /// mirror image of the leak this guards against, and just as wrong.
        /// </remarks>
        private static string? ReadValue(IProperty property, TEntity row, HashSet<string> redacted)
        {
            var value = AuditValueFormatter.Stringify(property.GetGetter().GetClrValue(row));

            return redacted.Contains(property.Name) ? AuditRedaction.Fingerprint(value) : value;
        }

        private static string BuildKey(IKey? key, TEntity row)
            => key is null
                ? string.Empty
                : string.Join(':', key.Properties.Select(
                    p => AuditValueFormatter.Stringify(p.GetGetter().GetClrValue(row)) ?? string.Empty));
    }

    private sealed class Scope<TEntity>(
        SetBasedAuditCapture owner,
        SnapshotReader<TEntity> reader,
        string module,
        IReadOnlyList<AuditRowSnapshot> before,
        int? truncatedRowCount) : ISetBasedAuditScope
        where TEntity : class
    {
        public async Task CompleteAsync(string? reason = null, CancellationToken ct = default)
        {
            var after = truncatedRowCount is null
                ? await reader.ReadAsync(ct)
                : Array.Empty<AuditRowSnapshot>();

            await owner.PublishAsync(
                module, reader.EntityTypeName, before, after, truncatedRowCount, reason, ct);
        }
    }
}
