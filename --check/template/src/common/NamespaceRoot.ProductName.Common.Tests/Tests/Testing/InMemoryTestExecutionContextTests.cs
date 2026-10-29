using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Tests.Fixtures;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Testing;

/// <summary>
/// The in-memory test context keeps one database per test across its scopes, as ADR-005 relies on: what is
/// arranged is seen by the act, what the act saves is seen by the assert, and two tests see nothing of
/// each other.
/// </summary>
[TestFixture]
[TestOf(typeof(InMemoryTestExecutionContext<,>))]
[Parallelizable(ParallelScope.All)]
public class InMemoryTestExecutionContextTests
{
    public sealed class Regions(CatalogueDbContext db)
    {
        public Task<int> CountAsync() => db.Regions.CountAsync();

        public async Task AddAsync(string name)
        {
            db.Regions.Add(new Region(Guid.NewGuid(), name));
            await db.SaveChangesAsync();
        }
    }

    [Test(Description = "What ArrangeAsync seeds, the act sees")]
    public async Task Should_SeeTheArrangedRows_When_Acting()
    {
        await using var ctx = new InMemoryTestExecutionContext<Regions, CatalogueDbContext>();
        await ctx.ArrangeAsync(new Region(Guid.NewGuid(), "North"));

        (await ctx.ActAsync(r => r.CountAsync())).ShouldBe(1);
    }

    [Test(Description = "What the act saves, the assert reads")]
    public async Task Should_ReadWhatTheActSaved_When_Asserting()
    {
        await using var ctx = new InMemoryTestExecutionContext<Regions, CatalogueDbContext>();

        await ctx.ActAsync(r => r.AddAsync("North"));

        await ctx.AssertAsync(async db => (await db.Regions.SingleAsync()).Name.ShouldBe("North"));
    }

    [Test(Description = "Two acts of one test share the database")]
    public async Task Should_SeeTheFirstAct_When_ActingAgain()
    {
        await using var ctx = new InMemoryTestExecutionContext<Regions, CatalogueDbContext>();

        await ctx.ActAsync(r => r.AddAsync("North"));
        await ctx.ActAsync(r => r.AddAsync("South"));

        (await ctx.ActAsync(r => r.CountAsync())).ShouldBe(2);
    }

    [Test(Description = "Two tests do not see each other's rows")]
    public async Task Should_StartEmpty_When_AnotherContextHasRows()
    {
        await using var first = new InMemoryTestExecutionContext<Regions, CatalogueDbContext>();
        await using var second = new InMemoryTestExecutionContext<Regions, CatalogueDbContext>();

        await first.ActAsync(r => r.AddAsync("North"));

        (await second.ActAsync(r => r.CountAsync())).ShouldBe(0);
    }
}
