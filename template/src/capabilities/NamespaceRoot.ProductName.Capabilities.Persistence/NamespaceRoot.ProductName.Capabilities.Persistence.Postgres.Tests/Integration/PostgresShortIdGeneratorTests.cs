using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Capabilities.Persistence.Postgres;
using PostgresServer = NamespaceRoot.ProductName.Framework.Testing.Integration.Postgres;

namespace NamespaceRoot.ProductName.Capabilities.Persistence.Postgres.Tests.Integration;

/// <summary>
/// Numbers from a real sequence: each one new, none handed out twice under concurrency, and a sequence
/// name that cannot change the statement.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
internal class PostgresShortIdGeneratorTests
{
    private string _sequence = null!;

    private sealed class Db(string connectionString) : DbContext(
        new DbContextOptionsBuilder<Db>().UseNpgsql(connectionString).Options);

    [SetUp]
    public async Task CreateSequence()
    {
        _sequence = $"public.test_seq_{Guid.NewGuid():N}";
        await using var db = new Db(PostgresServer.ConnectionString());
        // the name is made above from a Guid; a sequence name cannot be a SQL parameter
        await db.Database.ExecuteSqlRawAsync("CREATE SEQUENCE " + _sequence);
    }

    [TearDown]
    public async Task DropSequence()
    {
        await using var db = new Db(PostgresServer.ConnectionString());
        await db.Database.ExecuteSqlRawAsync("DROP SEQUENCE IF EXISTS " + _sequence);
    }

    [Test(Description = "Each call takes the next number")]
    public async Task Should_TakeTheNextNumber()
    {
        await using var db = new Db(PostgresServer.ConnectionString());
        var generator = new PostgresShortIdGenerator<Db>(db);

        (await generator.GetNextAsync(_sequence)).ShouldBe(1);
        (await generator.GetNextAsync(_sequence)).ShouldBe(2);
    }

    [Test(Description = "Callers at the same time never get the same number")]
    public async Task Should_NeverHandOutANumberTwice_When_CallersRace()
    {
        var numbers = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = new Db(PostgresServer.ConnectionString());
            return await new PostgresShortIdGenerator<Db>(db).GetNextAsync(_sequence);
        }));

        numbers.Distinct().Count().ShouldBe(20);
    }

    [Test(Description = "A name that is not a sequence fails; it is never run as SQL")]
    public async Task Should_Refuse_When_TheNameIsNotASequence()
    {
        await using var db = new Db(PostgresServer.ConnectionString());

        await Should.ThrowAsync<Npgsql.PostgresException>(() =>
            new PostgresShortIdGenerator<Db>(db).GetNextAsync("x'); DROP TABLE users; --"));
    }
}
