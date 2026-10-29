// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Attributes;
using NamespaceRoot.ProductName.Common.Contracts.Messaging.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;

/// <summary>
/// Captures changes to entities marked with <see cref="AuditableAttribute"/> and publishes
/// <see cref="AuditRecordedV1"/> for each of them.
/// </summary>
/// <remarks>
/// <para>
/// Publishing happens inside <c>SavingChangesAsync</c> on purpose: the bus outbox writes the message
/// through the same <see cref="DbContext"/>, so the audit row joins the very save that carries the
/// change. A mutation rolled back leaves no journal entry, and no second round-trip is needed.
/// </para>
/// <para>
/// <b>Note:</b> must be registered as a <b>Singleton</b> to support <b>DbContextPool</b>. The
/// constructor stays empty; scoped dependencies come from <see cref="AuditInfrastructureResolver"/>.
/// </para>
/// </remarks>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>Keeps the stored diff in the casing the API and the UI consume.</summary>
    private static readonly JsonSerializerOptions ChangesSerializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (data.Context is not DbContextBase context || !context.ChangeTracker.HasChanges())
            return await base.SavingChangesAsync(data, result, ct);

        var entries = context.ChangeTracker.Entries()
            .Where(e => GetAuditMarker(e.Entity.GetType()) is not null)
            .Where(HasRecordableChange)
            .ToList();

        if (entries.Count == 0)
            return await base.SavingChangesAsync(data, result, ct);

        if (!AuditInfrastructureResolver.TryResolve(context, out var execution, out var bus))
            return await base.SavingChangesAsync(data, result, ct);

        var (actorUserId, actorLogin, actorTenantId) = AuditActorReader.Read(execution);
        var occurredAt = execution.TimeProvider.GetUtcNow();
        var sourceService = context.Model.GetDefaultSchema() ?? context.GetType().Name;
        var traceId = Activity.Current?.TraceId.ToString();

        foreach (var entry in entries)
        {
            var marker = GetAuditMarker(entry.Entity.GetType())!;
            var changes = await BuildChangesAsync(entry, ct);

            // Nothing but ignored columns moved — recording "something changed" without saying what
            // only adds noise to the journal. Creations and deletions are events in themselves and
            // are kept even when the diff comes out empty.
            if (changes.Count == 0 && entry.State is not (EntityState.Added or EntityState.Deleted))
                continue;

            var auditEvent = new AuditRecordedV1
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = occurredAt,
                Module = marker.Module,
                EntityType = entry.Entity.GetType().Name,
                EntityId = BuildEntityId(entry),
                EntityDisplay = ReadDisplayValue(entry, marker),
                Action = ResolveAction(entry.State),
                Changes = JsonSerializer.Serialize(changes, ChangesSerializerOptions),
                ActorUserId = actorUserId,
                ActorLogin = actorLogin,
                ActorTenantId = actorTenantId,
                SubjectTenantId = ReadSubjectTenantId(entry, marker),
                SourceService = sourceService,
                TraceId = traceId,
                CorrelationId = Correlation.Current,
            };

            await bus.PublishAsync(auditEvent, ct);
        }

        return await base.SavingChangesAsync(data, result, ct);
    }

    private static AuditableAttribute? GetAuditMarker(Type entityType) =>
        entityType.GetCustomAttribute<AuditableAttribute>(inherit: false);

    private static string ResolveAction(EntityState state) => state switch
    {
        EntityState.Added => AuditAction.Created,
        EntityState.Deleted => AuditAction.Deleted,
        _ => AuditAction.Updated,
    };

    /// <summary>
    /// Whether this save carries anything worth recording for the entity — a change to its own row,
    /// or to a value object it owns.
    /// </summary>
    /// <remarks>
    /// An owned type is tracked as its own entry, so editing only an order's delivery address leaves
    /// the owner <see cref="EntityState.Unchanged"/>. Selecting on the owner's state alone would
    /// drop that change from the journal entirely.
    /// </remarks>
    private static bool HasRecordableChange(EntityEntry entry) =>
        IsMutation(entry.State) || OwnedEntries(entry).Any(owned => IsMutation(owned.Entry.State));

    private static bool IsMutation(EntityState state) =>
        state is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    /// <summary>
    /// Value objects the entity owns, paired with the navigation they hang off.
    /// </summary>
    private static IEnumerable<(string Path, EntityEntry Entry)> OwnedEntries(EntityEntry entry) =>
        entry.References
            .Where(reference => reference.TargetEntry?.Metadata.IsOwned() == true)
            .Select(reference => (reference.Metadata.Name, reference.TargetEntry!));

    /// <summary>
    /// Builds the recorded diff. Deletions carry none: the row is gone and the action already says so.
    /// </summary>
    private static async Task<Dictionary<string, AuditChange>> BuildChangesAsync(
        EntityEntry entry, CancellationToken ct)
    {
        if (entry.State == EntityState.Deleted)
            return [];

        var changes = new Dictionary<string, AuditChange>(StringComparer.Ordinal);
        var ownerIsNew = entry.State == EntityState.Added;

        await CollectIntoAsync(changes, entry, prefix: null, ownerIsNew, ct);

        // Owned values are folded into the owner's diff instead of getting a row of their own: a
        // value object has no identity to look up, and the operator is searching for "what changed
        // on this order", not for an address record.
        foreach (var (path, owned) in OwnedEntries(entry))
            await CollectIntoAsync(changes, owned, path, ownerIsNew, ct);

        return changes;
    }

    /// <summary>
    /// Adds one entry's recordable properties to the diff, prefixing them with the navigation when
    /// they belong to an owned value object (<c>DeliveryAddress.City</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A creation records every property it carries, not only those differing from the CLR default:
    /// "created with IsActive = false" and "IsActive was never set" are different statements, and
    /// the journal must be able to make the first. Nulls stay out — about those there is nothing to
    /// say.
    /// </para>
    /// <para>
    /// Whether this counts as a creation is decided by the OWNER, not by the entry. Services replace
    /// a value object wholesale rather than mutating it, and EF tracks the replacement as Added even
    /// though the row has been there all along. Taken at face value, replacing an address would report
    /// every field as appearing out of nowhere, losing where the order used to go. For that case the previous values are read back from the database: one extra
    /// select, on a save that happens rarely and is worth being precise about.
    /// </para>
    /// </remarks>
    private static async Task CollectIntoAsync(
        Dictionary<string, AuditChange> changes,
        EntityEntry entry,
        string? prefix,
        bool ownerIsNew,
        CancellationToken ct)
    {
        var excluded = ResolveExcludedProperties(entry);
        // Same resolver the set-based snapshot uses — a secret must not depend on which path wrote
        // the row for whether its value reaches the journal.
        var redacted = AuditRedaction.ResolveFor(entry.Entity.GetType());

        var stored = !ownerIsNew && entry.State == EntityState.Added
            ? await entry.GetDatabaseValuesAsync(ct)
            : null;

        foreach (var property in entry.Properties.OrderBy(p => p.Metadata.Name))
        {
            var name = property.Metadata.Name;

            if (property.Metadata.IsShadowProperty() || excluded.Contains(name))
                continue;

            var original = ResolveOriginal(property, entry, stored, ownerIsNew, name);
            var current = property.CurrentValue;

            if (ownerIsNew && current is null)
                continue;

            var (before, after) = (Stringify(original), Stringify(current));

            // Compared as recorded, not by Equals: a collection compares by reference there, so
            // re-assigning an identical tree path produced an entry reading "[1] → [1]": an update
            // that changed nothing, filed against a tenant who did nothing.
            if (!ownerIsNew && string.Equals(before, after, StringComparison.Ordinal))
                continue;

            changes[prefix is null ? name : $"{prefix}.{name}"] = redacted.Contains(name)
                ? new AuditChange(AuditRedactAttribute.Placeholder, AuditRedactAttribute.Placeholder)
                : new AuditChange(before, after);
        }
    }

    /// <summary>
    /// The value a property held before this save: nothing on a genuine creation, the stored row for
    /// a replaced value object, and EF's own original otherwise.
    /// </summary>
    private static object? ResolveOriginal(
        PropertyEntry property,
        EntityEntry entry,
        PropertyValues? stored,
        bool ownerIsNew,
        string name)
    {
        if (ownerIsNew) return null;

        if (stored is not null)
            return stored.Properties.Any(p => p.Name == name) ? stored[name] : null;

        return entry.State == EntityState.Added ? null : property.OriginalValue;
    }

    /// <summary>
    /// Infrastructure columns, key columns, concurrency tokens and explicitly excluded properties.
    /// Concurrency tokens and the key are read from EF metadata rather than by name — they are
    /// spelled differently per entity.
    /// </summary>
    private static HashSet<string> ResolveExcludedProperties(EntityEntry entry)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in entry.Metadata.GetProperties())
        {
            if (AuditColumnPolicy.IsAlwaysIgnored(property.Name) || property.IsConcurrencyToken)
                excluded.Add(property.Name);
        }

        // The key is already the EntityId of the record. Repeating it inside the diff of a creation
        // says "the id changed from nothing to itself", which is noise on every single row created.
        foreach (var property in entry.Metadata.FindPrimaryKey()?.Properties ?? [])
            excluded.Add(property.Name);

        foreach (var property in entry.Entity.GetType().GetProperties())
        {
            if (property.GetCustomAttribute<AuditIgnoreAttribute>(inherit: false) is not null)
                excluded.Add(property.Name);
        }

        return excluded;
    }

    /// <summary>
    /// Primary key rendered as text — keys are not uniformly <see cref="Guid"/> across services,
    /// and the journal never joins on this value.
    /// </summary>
    private static string BuildEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return string.Empty;

        var parts = key.Properties
            .Select(p => Stringify(entry.Property(p.Name).CurrentValue) ?? string.Empty);

        return string.Join(':', parts);
    }

    private static string? ReadDisplayValue(EntityEntry entry, AuditableAttribute marker)
    {
        if (marker.DisplayProperty is null) return null;

        var property = entry.Entity.GetType().GetProperty(marker.DisplayProperty);

        return property is null ? null : Stringify(property.GetValue(entry.Entity));
    }

    /// <summary>
    /// The tenant the changed row belongs to, read from the property the entity nominates. Left null
    /// when the entity nominates none, when the property is missing, or when it holds no tenant.
    /// </summary>
    /// <remarks>
    /// Deliberately the CURRENT value, including on a transfer: the row now belongs to its new
    /// tenant, and the previous one has no claim to what happens under someone else afterwards.
    /// </remarks>
    private static Guid? ReadSubjectTenantId(EntityEntry entry, AuditableAttribute marker)
    {
        if (marker.SubjectTenantProperty is null) return null;

        var property = entry.Entity.GetType().GetProperty(marker.SubjectTenantProperty);

        return property?.GetValue(entry.Entity) switch
        {
            Guid id when id != Guid.Empty => id,
            _ => null,
        };
    }

    private static string? Stringify(object? value) => AuditValueFormatter.Stringify(value);

    private sealed record AuditChange(string? Old, string? New);
}
