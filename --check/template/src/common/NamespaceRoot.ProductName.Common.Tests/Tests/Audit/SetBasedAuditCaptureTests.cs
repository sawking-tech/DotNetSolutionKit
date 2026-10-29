using NamespaceRoot.ProductName.Common.Application.Auditing;
using NamespaceRoot.ProductName.Common.Application.Messaging;
using NamespaceRoot.ProductName.Common.Attributes;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Messaging;
using NamespaceRoot.ProductName.Common.Contracts.Messaging.Audit;
using NamespaceRoot.ProductName.Common.Domain;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;
using NamespaceRoot.ProductName.Common.Tests.Audit;
using NamespaceRoot.ProductName.Common.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Audit;

/// <summary>
/// Snapshots taken around a set-based write, which the change tracker never sees.
/// </summary>
/// <remarks>
/// The interceptor has its own tests; this path shares none of its code. It reads values straight
/// off EF's model metadata, so every rule the interceptor enforces has to be shown to hold here
/// independently — redaction above all, since a snapshot is serialised into a message and lands in
/// the journal verbatim.
/// </remarks>
[TestFixture]
[TestOf(typeof(SetBasedAuditCapture))]
[Parallelizable(ParallelScope.All)]
internal sealed class SetBasedAuditCaptureTests
{
    private const string Secret = "AQAAAAIAAYagAAAAEC-hash-material";

    [Test(Description = "A redacted column never leaves in the clear — the snapshot carries a fingerprint of it instead of the value")]
    public async Task Should_NotCarryTheValue_When_ColumnIsRedacted()
    {
        await using var harness = new CaptureHarness();

        await harness.SeedAsync(new SecretiveEntity("key-1", Secret, "Active"));
        await harness.CaptureAsync(update: entity => entity.Rotate("rotated-secret"));

        var recorded = harness.Published.ShouldHaveSingleItem();

        // The whole message, not just the field: a leak anywhere in it is a leak.
        System.Text.Json.JsonSerializer.Serialize(recorded).ShouldNotContain(Secret);
        recorded.Before.ShouldHaveSingleItem().Values["Token"].ShouldStartWith(AuditRedactAttribute.Placeholder);
        recorded.After.ShouldHaveSingleItem().Values["Token"].ShouldStartWith(AuditRedactAttribute.Placeholder);
    }

    [Test(Description = "A rotated secret still reads as changed — blanking both sides would hide the rotation, which is the event worth recording")]
    public async Task Should_DifferBetweenSnapshots_When_RedactedValueChanges()
    {
        await using var harness = new CaptureHarness();

        await harness.SeedAsync(new SecretiveEntity("key-1", Secret, "Active"));
        await harness.CaptureAsync(update: entity => entity.Rotate("rotated-secret"));

        var recorded = harness.Published.ShouldHaveSingleItem();

        recorded.Before[0].Values["Token"].ShouldNotBe(recorded.After[0].Values["Token"]);
    }

    [Test(Description = "An untouched secret reads as unchanged — the fingerprint is stable within the pair of snapshots")]
    public async Task Should_MatchAcrossSnapshots_When_RedactedValueIsUntouched()
    {
        await using var harness = new CaptureHarness();

        await harness.SeedAsync(new SecretiveEntity("key-1", Secret, "Active"));
        await harness.CaptureAsync(update: entity => entity.Block());

        var recorded = harness.Published.ShouldHaveSingleItem();

        recorded.Before[0].Values["Token"].ShouldBe(recorded.After[0].Values["Token"]);
        recorded.Before[0].Values["Status"].ShouldBe("Active");
        recorded.After[0].Values["Status"].ShouldBe("Blocked");
    }

    [Test(Description = "An unset secret stays unset — collapsing null into a fingerprint would make 'never had one' look like 'has one'")]
    public async Task Should_KeepNull_When_RedactedValueIsAbsent()
    {
        await using var harness = new CaptureHarness();

        await harness.SeedAsync(new SecretiveEntity("key-1", token: null, "Active"));
        await harness.CaptureAsync(update: entity => entity.Block());

        harness.Published.ShouldHaveSingleItem().Before[0].Values["Token"].ShouldBeNull();
    }

    [Test(Description = "Ordinary columns are recorded as they are — redaction applies to what is marked, not to everything")]
    public async Task Should_RecordPlainValues_When_ColumnIsNotRedacted()
    {
        await using var harness = new CaptureHarness();

        await harness.SeedAsync(new SecretiveEntity("key-1", Secret, "Active"));
        await harness.CaptureAsync(update: entity => entity.Block());

        harness.Published.ShouldHaveSingleItem().Before[0].Values["Name"].ShouldBe("key-1");
    }

    #region Harness

    private sealed class CaptureHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public CaptureHarness()
        {
            var timeProvider = new TimeProviderMock(DateTimeOffset.UtcNow);
            var actor = UserContextMockFactory.CreateJwtUser(timeProvider);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddAuditPersistence();
            services.AddScoped<IDomainExecutionContext>(_ => new TestDomainExecutionContext(actor, timeProvider));
            services.AddScoped<IMessageBus>(_ => new RecordingBus(Published));
            services.AddDbContext<CaptureDbContext>(options => options.UseInMemoryDatabase(DatabaseName));
            services.AddAuditRecorder<CaptureDbContext>("tests");

            _provider = services.BuildServiceProvider();
        }

        /// <summary>Fixed per harness so seed and capture share one store; unique across fixtures.</summary>
        private string DatabaseName { get; } = $"set-based-audit-{Guid.NewGuid()}";

        public List<AuditBulkRecordedV1> Published { get; } = [];

        public async Task SeedAsync(SecretiveEntity entity)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();

            db.Secretive.Add(entity);
            await db.SaveChangesAsync(CancellationToken.None);

            // The insert goes through the interceptor as any other write does; only what the
            // set-based capture publishes is under test here.
            Published.Clear();
        }

        /// <summary>
        /// Runs the shape a repository does: snapshot, mutate, snapshot, publish.
        /// </summary>
        /// <remarks>
        /// The mutation is an ordinary tracked save rather than <c>ExecuteUpdateAsync</c> — the
        /// in-memory provider cannot translate that — but the capture never looks at the tracker, so
        /// what it reads is the same either way.
        /// </remarks>
        public async Task CaptureAsync(Action<SecretiveEntity> update)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
            var capture = scope.ServiceProvider.GetRequiredService<ISetBasedAuditCapture>();

            var affected = db.Secretive.Where(x => x.Status != "Archived");
            var auditScope = await capture.BeginAsync(affected, "Testing", ct: CancellationToken.None);

            foreach (var entity in await db.Secretive.ToListAsync(CancellationToken.None))
                update(entity);

            await db.SaveChangesAsync(CancellationToken.None);

            // Only the bulk message is asserted on; the tracked save above emits its own entries.
            Published.Clear();
            await auditScope.CompleteAsync(ct: CancellationToken.None);
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }

    private sealed class RecordingBus(List<AuditBulkRecordedV1> sink) : IMessageBus
    {
        public Task PublishAsync<T>(T busEvent, CancellationToken ct = default)
            where T : class, IBusEvent
        {
            if (busEvent is AuditBulkRecordedV1 bulk) sink.Add(bulk);
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T command, CancellationToken ct = default)
            where T : class, IBusCommand => Task.CompletedTask;
    }

    private sealed class CaptureDbContext(DbContextOptions<CaptureDbContext> options) : DbContextBase(options)
    {
        public DbSet<SecretiveEntity> Secretive => Set<SecretiveEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SecretiveEntity>().HasKey(e => e.Id);
    }

    /// <summary>An audited row that holds key material — the case redaction exists for.</summary>
    [Auditable("Testing", DisplayProperty = nameof(Name))]
    private sealed class SecretiveEntity : Entity<Guid>
    {
        /// <summary>EF Core constructor.</summary>
        private SecretiveEntity() { }

        public SecretiveEntity(string name, string? token, string status)
        {
            Id = Guid.NewGuid();
            Name = name;
            Token = token;
            Status = status;
        }

        public string Name { get; private set; } = null!;

        [AuditRedact]
        public string? Token { get; private set; }

        public string Status { get; private set; } = null!;

        public void Rotate(string token) => Token = token;

        public void Block() => Status = "Blocked";
    }

    #endregion
}
