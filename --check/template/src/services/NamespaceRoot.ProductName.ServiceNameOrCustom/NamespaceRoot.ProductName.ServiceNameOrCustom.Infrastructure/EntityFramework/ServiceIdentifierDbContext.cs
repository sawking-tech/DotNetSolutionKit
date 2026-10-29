using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Domain.Persistence;
//#if (Messaging == "outbox")
using NamespaceRoot.ProductName.Common.Infrastructure.Messaging;
//#endif
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework;

[SuppressMessage("ReSharper", "RedundantExtendsListEntry")]
public class ServiceIdentifierDbContext(DbContextOptions<ServiceIdentifierDbContext> options)
    : DbContextBase(options), IUnitOfWork
{
    public static readonly string DefaultSchemaName = "servicenameorcustom";

    // Add DbSet here

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(DefaultSchemaName);
//#if (Messaging == "outbox")

        // Outbox tables in this service's schema: a message is stored with the change that caused it
        modelBuilder.AddTransactionalOutbox(DefaultSchemaName);
//#endif

        // Automatic registration of configurations from assembly
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
    }
}
