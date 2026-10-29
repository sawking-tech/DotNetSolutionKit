using System.Data;
using LinqSpecs;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Domain.Specifications;
using NamespaceRoot.ProductName.Common.Infrastructure.Diagnostics;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Specifications;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.SqlServer;
using NamespaceRoot.ProductName.Common.Tests.Stubs;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// The SQL Server pieces against a real SQL Server, from <c>TEST_SQLSERVER</c>, in a database of the fixture's
/// own: the migration lock, the schema guard, the short ids, unique violations, the search and its in-memory
/// stand-in, and the SQL of the outbox statistics.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
[NonParallelizable]
internal class SqlServerPersistenceTests
{
    private string _database = null!;
    private string _connectionString = null!;

    private sealed class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string[] Tags { get; set; } = [];
    }

    private sealed class Db(string connectionString) : DbContext
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlServer(connectionString);

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<Customer>().HasIndex(c => c.Email).IsUnique();
    }

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        var server = SqlServer.ConnectionString();
        _database = $"tests_{Guid.NewGuid():N}";
        await using (var master = new SqlConnection(server))
        {
            await master.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{_database}]", master).ExecuteNonQueryAsync();
        }

        _connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = _database }.ConnectionString;
        await using var db = new Db(_connectionString);
        await db.Database.EnsureCreatedAsync();
        db.Customers.AddRange(
            new Customer { Name = "Иван Петров", Email = "ivan@example.com", Tags = ["VIP", "москва"] },
            new Customer { Name = "Anna [admin]", Email = "anna@example.com", Tags = ["new"] },
            new Customer { Name = "100% off_sale", Email = "sale@example.com", Tags = [] });
        await db.SaveChangesAsync();
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        if (_database is null)
            return;
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(SqlServer.ConnectionString());
        await master.OpenAsync();
        await new SqlCommand($"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]", master)
            .ExecuteNonQueryAsync();
    }

    private static IQueryable<Customer> Search(IQueryable<Customer> customers, ICaseInsensitiveSearch search, string pattern) =>
        customers.Where(search.GetSpecification<Customer>(c => c.Name, pattern).ToExpression());

    [TestCase("иван", ExpectedResult = new[] { "ivan@example.com" }, Description = "Cyrillic, other case")]
    [TestCase("ANNA", ExpectedResult = new[] { "anna@example.com" })]
    [TestCase("[admin]", ExpectedResult = new[] { "anna@example.com" }, Description = "a bracket is a character")]
    [TestCase("100%", ExpectedResult = new[] { "sale@example.com" }, Description = "a percent sign is a character")]
    [TestCase("f_s", ExpectedResult = new[] { "sale@example.com" }, Description = "an underscore is a character")]
    [TestCase("x_y", ExpectedResult = new string[0])]
    public string[] The_search_finds_what_it_is_given_case_insensitively(string pattern)
    {
        using var db = new Db(_connectionString);
        return Search(db.Customers, new SqlServerCaseInsensitiveSearch(), pattern).Select(c => c.Email).OrderBy(e => e).ToArray();
    }

    [TestCase("иван")]
    [TestCase("ANNA")]
    [TestCase("[admin]")]
    [TestCase("100%")]
    [TestCase("f_s")]
    [TestCase("x_y")]
    [TestCase("")]
    [TestCase("anna ")]
    [TestCase(" anna")]
    public void The_in_memory_search_answers_as_SQL_Server_does(string pattern)
    {
        using var db = new Db(_connectionString);
        var onServer = Search(db.Customers, new SqlServerCaseInsensitiveSearch(), pattern).Select(c => c.Email).OrderBy(e => e).ToArray();
        var inMemory = Search(db.Customers.ToList().AsQueryable(), new InMemorySqlServerCaseInsensitiveSearch(), pattern)
            .Select(c => c.Email).OrderBy(e => e).ToArray();

        inMemory.ShouldBe(onServer);
    }

    [Test]
    public void The_search_looks_into_an_array_of_strings()
    {
        using var db = new Db(_connectionString);
        var spec = new SqlServerCaseInsensitiveSearch().GetArraySpecification<Customer>(c => c.Tags, "МОСК");

        db.Customers.Where(spec.ToExpression()).Select(c => c.Email).ToArray().ShouldBe(["ivan@example.com"]);
    }

    [Test]
    public async Task A_second_email_of_the_same_value_is_a_unique_violation()
    {
        await using var db = new Db(_connectionString);
        db.Customers.Add(new Customer { Name = "Copy", Email = "ivan@example.com" });

        var failure = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        SqlServerErrors.IsUniqueViolation(failure).ShouldBeTrue();
    }

    [Test]
    public void The_migration_lock_keeps_a_second_session_out()
    {
        var migrationLock = new SqlServerMigrationLock();
        using var first = migrationLock.Connect(_connectionString);
        migrationLock.Acquire(first, 42);

        using var second = new SqlConnection(_connectionString);
        second.Open();
        TryLock(second, 42).ShouldBeLessThan(0, "the lock is held by the first session");

        migrationLock.Release(first, 42);
        TryLock(second, 42).ShouldBeGreaterThanOrEqualTo(0, "released, the lock is free");
    }

    private static int TryLock(SqlConnection connection, long key)
    {
        using var command = new SqlCommand("sp_getapplock", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(new SqlParameter("@Resource", $"migrations:{key}"));
        command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
        command.Parameters.Add(new SqlParameter("@LockOwner", "Session"));
        // Not new SqlParameter(name, 0): a literal 0 picks the (name, SqlDbType) overload and sends NULL.
        command.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = 0 });
        var result = command.Parameters.Add(new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue });
        command.ExecuteNonQuery();
        return (int)result.Value;
    }

    [Test]
    public void The_schema_guard_claims_a_schema_for_one_service()
    {
        var schema = $"orders_{Guid.NewGuid():N}"[..20];

        SqlServerSchemaGuard.EnsureExclusiveSchema(_connectionString, schema, "Orders");
        SqlServerSchemaGuard.EnsureExclusiveSchema(_connectionString, schema, "Orders");

        Should.Throw<InvalidOperationException>(() => SqlServerSchemaGuard.EnsureExclusiveSchema(_connectionString, schema, "Billing"))
            .Message.ShouldContain("already owned by 'Orders'");
    }

    [Test]
    public async Task Each_short_id_is_the_next_number()
    {
        var sequence = $"dbo.seq_{Guid.NewGuid():N}";
        await using var db = new Db(_connectionString);
        await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE " + sequence + " AS bigint START WITH 100");
        var generator = new SqlServerShortIdGenerator<Db>(db);

        (await generator.GetNextAsync(sequence)).ShouldBe(100);
        (await generator.GetNextAsync(sequence)).ShouldBe(101);
    }

    [Test]
    public async Task The_outbox_statistics_read_pending_and_sent_rows()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await new SqlCommand(
            "CREATE TABLE dbo.OutboxMessage (MessageId uniqueidentifier NOT NULL, EnqueueTime datetime2 NULL, SentTime datetime2 NOT NULL, Body nvarchar(max) NOT NULL); " +
            "INSERT INTO dbo.OutboxMessage VALUES (NEWID(), '2026-10-01', '0001-01-01', 'order-1'), (NEWID(), '2026-10-02', '2026-10-02 10:00', 'order-2')",
            connection).ExecuteNonQueryAsync();
        var dialect = new SqlServerOutboxStatsDialect();

        await using (var totals = new SqlCommand(dialect.TotalsSql("[dbo].[OutboxMessage]"), connection).ExecuteReader())
        {
            totals.Read().ShouldBeTrue();
            totals.GetInt64(0).ShouldBe(1, "pending");
            totals.GetInt64(1).ShouldBe(1, "sent");
        }

        await using var sample = new SqlCommand(dialect.SampleSql("[dbo].[OutboxMessage]"), connection);
        sample.Parameters.AddRange(dialect.SampleParameters("order-1", 10));
        await using var rows = sample.ExecuteReader();
        rows.Read().ShouldBeTrue();
        rows.IsDBNull(2).ShouldBeTrue("a pending row has no sent time");
        rows.Read().ShouldBeFalse("the filter keeps one row");
    }
}
