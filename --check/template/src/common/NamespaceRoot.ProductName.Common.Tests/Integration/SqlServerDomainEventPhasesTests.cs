using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using static NamespaceRoot.ProductName.Common.Tests.Tests.Events.DomainEventPhasesOnSaveTests;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// The phases after a write on a real database: a save with no transaction around it, which EF Core sends
/// as a single statement without a transaction, a commit and a rollback each run their phase once.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
internal class SqlServerDomainEventPhasesTests
{
    private sealed class Context : SqlServerIntegrationTestBase<Writes, ThingsDb>
    {
        private Context(string admin, string name, string connection, Action<IServiceCollection>? configure)
            : base(admin, name, connection, configure) { }

        public static async Task<Context> CreateAsync(Phases phases)
        {
            var context = await CreateCoreAsync<Context>(SqlServer.ConnectionString(), "common_events",
                (admin, name, connection, configure) => new Context(admin, name, connection, configure),
                services =>
                {
                    services.AddSingleton(phases);
                    services.AddLogging();
                    services.AddDomainEvents(typeof(BeforeSave), typeof(AfterCommit), typeof(OnRollback));
                });
            // The model has no migrations, and the database cloned from the migrated template already has the
            // history table, so EnsureCreated would see tables and create none.
            await context.ExecuteAsync<ThingsDb>(db => db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync());
            return context;
        }
    }

    [Test(Description = "A single-statement save with no transaction around it runs the post-commit phase")]
    public async Task Should_RunPostCommit_When_ASaveCommitsOnItsOwn()
    {
        var phases = new Phases();
        await using var ctx = await Context.CreateAsync(phases);

        await ctx.ActAsync(w => w.SaveAsync());

        phases.Seen.ShouldBe(["pre-save", "post-commit"]);
    }

    [Test(Description = "A commit runs the post-commit phase once")]
    public async Task Should_RunPostCommitOnce_When_ATransactionCommits()
    {
        var phases = new Phases();
        await using var ctx = await Context.CreateAsync(phases);

        await ctx.ActAsync(w => w.CommitAsync());

        phases.Seen.ShouldBe(["pre-save", "post-commit"]);
    }

    [Test(Description = "A rollback runs the rollback phase, and no post-commit")]
    public async Task Should_RunRollback_When_ATransactionRollsBack()
    {
        var phases = new Phases();
        await using var ctx = await Context.CreateAsync(phases);

        await ctx.ActAsync(w => w.RollBackAsync());

        phases.Seen.ShouldBe(["pre-save", "rollback"]);
    }
}
