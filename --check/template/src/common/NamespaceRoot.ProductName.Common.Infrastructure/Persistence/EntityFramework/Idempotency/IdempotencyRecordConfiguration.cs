using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NamespaceRoot.ProductName.Common.Domain.Idempotency;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Idempotency;

/// <summary>
/// The idempotency log's table, the same in every service that keeps one.
/// </summary>
/// <remarks>
/// Declared once here rather than in each service: the unique index is the whole mechanism, and a service
/// that declared it non-unique would still pass its own tests while letting duplicates through.
/// </remarks>
public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(record => record.Scope).HasColumnName("scope").HasMaxLength(64).IsRequired();
        builder.Property(record => record.Key).HasColumnName("key").HasMaxLength(IdempotencyKeyLimits.MaxLength).IsRequired();
        builder.Property(record => record.Operation).HasColumnName("operation").HasMaxLength(128).IsRequired();

        // A response body, stored whole: truncated, it would replay something the client never received.
        builder.Property(record => record.Response).HasColumnName("response").IsRequired();

        builder.Property(record => record.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(record => record.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // The mechanism, not an optimisation: two requests with one key race to insert, and the index
        // decides which of them did the work.
        builder.HasIndex(record => new { record.Scope, record.Key })
            .IsUnique()
            .HasDatabaseName("ix_idempotency_records_scope_key");
    }
}
