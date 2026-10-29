using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Configuration;

/// <summary>
/// Switching a dependency off in configuration: what is on by default, what goes off with the database,
/// and what a switched-off database answers.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class DependencySwitchesTests
{
    private static DependencySwitches Switches(bool busNeedsDatabase = false, params (string Key, string Value)[] settings) =>
        DependencySwitches.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .AddDependencyOverrides(busNeedsDatabase)
            .Build());

    [Test(Description = "Every dependency is on unless configuration says otherwise")]
    public void Should_SwitchEverythingOn_When_NothingIsConfigured()
    {
        var switches = Switches();

        switches.Database.ShouldBeTrue();
        switches.Jobs.ShouldBeTrue();
        switches.Bus.ShouldBeTrue();
        switches.SwitchedOff.ShouldBeEmpty();
    }

    [Test(Description = "The job server goes off with the database: the database is its storage")]
    public void Should_SwitchJobsOff_When_TheDatabaseIsOff()
    {
        var switches = Switches(settings: (DependencySwitches.DatabaseKey, "false"));

        switches.Jobs.ShouldBeFalse();
        switches.SwitchedOff.ShouldBe([DependencySwitches.DatabaseKey, DependencySwitches.JobsKey]);
    }

    [Test(Description = "A bus delivering through the outbox goes off with the database")]
    public void Should_SwitchAnOutboxBusOff_When_TheDatabaseIsOff() =>
        Switches(busNeedsDatabase: true, (DependencySwitches.DatabaseKey, "false")).Bus.ShouldBeFalse();

    [Test(Description = "A bus sending straight to the broker stays on without the database")]
    public void Should_KeepADirectBusOn_When_TheDatabaseIsOff() =>
        Switches(busNeedsDatabase: false, (DependencySwitches.DatabaseKey, "false")).Bus.ShouldBeTrue();

    [Test(Description = "Each switch works on its own while the database is on")]
    public void Should_SwitchOneDependencyOff_When_ItIsConfiguredOff()
    {
        var switches = Switches(busNeedsDatabase: true, (DependencySwitches.JobsKey, "false"));

        switches.Database.ShouldBeTrue();
        switches.Bus.ShouldBeTrue();
        switches.SwitchedOff.ShouldBe([DependencySwitches.JobsKey]);
    }

    private sealed class Catalogue() : DbContext(new DbContextOptionsBuilder<Catalogue>()
        .UseSolutionDatabase(SwitchedOffDatabase.ConnectionString)
        .UseSwitchedOffDatabase()
        .Options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    private sealed class Item
    {
        public int Id { get; set; }
    }

    [Test(Description = "A query to a switched-off database answers 503 and names the setting")]
    public async Task Should_AnswerServiceUnavailable_When_TheDatabaseIsSwitchedOff()
    {
        await using var db = new Catalogue();

        var error = await Should.ThrowAsync<ServiceUnavailableException>(() => db.Items.ToListAsync());

        error.Message.ShouldContain(DependencySwitches.DatabaseKey);
    }

    [Test(Description = "A write to a switched-off database answers 503 too")]
    public async Task Should_AnswerServiceUnavailable_When_WritingToASwitchedOffDatabase()
    {
        await using var db = new Catalogue();
        db.Items.Add(new Item());

        await Should.ThrowAsync<ServiceUnavailableException>(() => db.SaveChangesAsync());
    }
}
