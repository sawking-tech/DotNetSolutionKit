using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Infrastructure.Diagnostics;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence;
using NamespaceRoot.ProductName.Common.Infrastructure.Messaging;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Diagnostics;

/// <summary>
/// The outbox diagnostics put the table name into raw SQL, so it has to come from the model of the
/// context and be quoted by the provider, never from a caller.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class OutboxStatsQueryTests
{
    // Building the model needs no database: the connection string is never opened.
    private const string NoDatabase = "Server=localhost;Database=never-opened";

    // EF caches a model per context type, so each schema gets a type of its own.
    private abstract class WithOutbox<TSelf>(string schema) : DbContext(
        new DbContextOptionsBuilder<TSelf>().UseSolutionDatabase(NoDatabase).Options) where TSelf : DbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.AddTransactionalOutbox(schema);
    }

    private sealed class OrdersOutbox() : WithOutbox<OrdersOutbox>("orders");

    private sealed class OddSchemaOutbox() : WithOutbox<OddSchemaOutbox>("odd\"schema");

    private sealed class WithoutOutbox() : DbContext(
        new DbContextOptionsBuilder<WithoutOutbox>().UseSolutionDatabase(NoDatabase).Options);

    [Test(Description = "The table is the one the model maps, in the service's schema")]
    public void Should_ResolveTheMappedTable()
    {
        using var db = new OrdersOutbox();

        // Each provider quotes as it does: SQL Server always, PostgreSQL only what needs it.
        var expected = DatabaseProvider.IsSqlServer ? "[orders].[outbox_message]" : "orders.outbox_message";
        OutboxStatsQuery.ResolveOutboxTable(db).ShouldBe(expected);
    }

    [Test(Description = "A schema name that is not a plain identifier is quoted, its quote doubled")]
    public void Should_QuoteTheSchema_When_ItIsNotAPlainIdentifier()
    {
        using var db = new OddSchemaOutbox();

        var expected = DatabaseProvider.IsSqlServer
            ? "[odd\"schema].[outbox_message]"
            : "\"odd\"\"schema\".outbox_message";
        OutboxStatsQuery.ResolveOutboxTable(db).ShouldBe(expected);
    }

    [Test(Description = "A context without an outbox says what to add instead of querying a missing table")]
    public void Should_Throw_When_TheContextHasNoOutbox()
    {
        using var db = new WithoutOutbox();

        Should.Throw<InvalidOperationException>(() => OutboxStatsQuery.ResolveOutboxTable(db))
            .Message.ShouldContain("AddTransactionalOutbox");
    }
}
