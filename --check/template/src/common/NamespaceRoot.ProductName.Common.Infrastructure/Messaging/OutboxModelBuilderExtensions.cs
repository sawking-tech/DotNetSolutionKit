using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

/// <summary>
/// Extension methods for configuring MassTransit Transactional Outbox tables in EF Core model.
/// </summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Adds MassTransit Transactional Outbox tables (inbox_state, outbox_message, outbox_state)
    /// to the EF Core model under the specified schema.
    /// Call this in OnModelCreating when using AddMessaging&lt;TDbContext&gt; overload.
    /// </summary>
    public static ModelBuilder AddTransactionalOutbox(this ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.AddInboxStateEntity(e => e.ToTable("inbox_state", schema));
        modelBuilder.AddOutboxMessageEntity(e => e.ToTable("outbox_message", schema));
        modelBuilder.AddOutboxStateEntity(e => e.ToTable("outbox_state", schema));
        return modelBuilder;
    }
}
