using MassTransit;

namespace NamespaceRoot.ProductName.Capabilities.Messaging.RabbitMQ;

/// <summary>The lock statements the outbox runs against the solution's database.</summary>
internal static class OutboxDatabaseLocks
{
    public static IEntityFrameworkOutboxConfigurator UseDatabaseLocks(this IEntityFrameworkOutboxConfigurator outbox)
    {
//#if (Database != "mssql")
        outbox.UsePostgres();
//#endif
//#if (Database == "mssql")
        outbox.UseSqlServer();
//#endif
        return outbox;
    }
}
