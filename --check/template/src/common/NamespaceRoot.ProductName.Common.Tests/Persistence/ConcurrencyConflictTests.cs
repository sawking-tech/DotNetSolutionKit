using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;

namespace NamespaceRoot.ProductName.Common.Tests.Persistence;

/// <summary>
/// A write that loses a race on a concurrency token reaches the caller as <see cref="ConcurrencyException"/>,
/// which the API answers with 409, not as the EF exception, which it would answer with 500.
/// </summary>
[TestFixture]
[TestOf(typeof(DbContextBase))]
public class ConcurrencyConflictTests
{
    public sealed class Note
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = string.Empty;

        [ConcurrencyCheck]
        public Guid Version { get; set; }
    }

    public sealed class NotesDbContext(DbContextOptions options) : DbContextBase(options)
    {
        public DbSet<Note> Notes => Set<Note>();
    }

    private static NotesDbContext Open(string database) =>
        new(new DbContextOptionsBuilder().UseInMemoryDatabase(database).Options);

    [Test(Description = "The second of two writes of one version fails with ConcurrencyException")]
    public async Task Should_ThrowConcurrencyException_When_TheRowChangedSinceItWasRead()
    {
        var database = $"notes-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        await using (var seed = Open(database))
        {
            seed.Notes.Add(new Note { Id = id, Text = "draft", Version = Guid.NewGuid() });
            await seed.SaveChangesAsync();
        }

        await using var first = Open(database);
        await using var second = Open(database);
        var mine = await first.Notes.SingleAsync(n => n.Id == id);
        var theirs = await second.Notes.SingleAsync(n => n.Id == id);

        theirs.Text = "theirs";
        theirs.Version = Guid.NewGuid();
        await second.SaveChangesAsync();

        mine.Text = "mine";
        mine.Version = Guid.NewGuid();
        var refused = await Should.ThrowAsync<ConcurrencyException>(() => first.SaveChangesAsync());
        refused.InnerException.ShouldBeOfType<DbUpdateConcurrencyException>();
    }
}
