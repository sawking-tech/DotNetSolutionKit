using System.Text.Json;
using NamespaceRoot.ProductName.Common.Attributes;
using NamespaceRoot.ProductName.Common.Application.Messaging;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Messaging;
using NamespaceRoot.ProductName.Common.Contracts.Messaging.Audit;
using NamespaceRoot.ProductName.Common.Domain;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using NamespaceRoot.ProductName.Common.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Audit;

/// <summary>
/// Capture behaviour of <see cref="AuditSaveChangesInterceptor"/>: what reaches the journal,
/// what is deliberately left out, and how the actor is recorded.
/// </summary>
[TestFixture]
[TestOf(typeof(AuditSaveChangesInterceptor))]
[Parallelizable(ParallelScope.All)]
public class AuditSaveChangesInterceptorTests
{
    [Test(Description = "Adding an entity marked as auditable records a Created entry carrying the module, display value and initial field values")]
    public async Task Should_RecordCreated_When_AuditableEntityIsAdded()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-001", "Draft"));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var entry = harness.Published.ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.Created);
        entry.Module.ShouldBe("Testing");
        entry.EntityType.ShouldBe(nameof(AuditedEntity));
        entry.EntityDisplay.ShouldBe("ORD-001");
        entry.SourceService.ShouldNotBeNullOrWhiteSpace();

        var changes = Deserialize(entry.Changes);
        changes.ShouldContainKey("Name");
        changes["Name"].New.ShouldBe("ORD-001");
    }

    [Test(Description = "An entry carries the correlation identifier of the request behind it, so the journal and the logs are searched by one value")]
    public async Task Should_RecordCorrelation_When_RequestHasOne()
    {
        await using var harness = new AuditHarness();

        using (NamespaceRoot.ProductName.Common.Application.Tracing.Correlation.Use("client-chosen-42"))
        {
            await harness.ExecuteAsync(async (db, execution) =>
            {
                db.Audited.Add(new AuditedEntity(execution, "ORD-010", "Draft"));
                await db.SaveChangesAsync(CancellationToken.None);
            });
        }

        harness.Published.ShouldHaveSingleItem().CorrelationId.ShouldBe("client-chosen-42");
    }

    [Test(Description = "The key of a created row is reported as its EntityId and left out of the diff — inside the diff it would only ever read as changed from nothing to itself")]
    public async Task Should_OmitPrimaryKeyFromDiff_When_EntityIsCreated()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-002", "Draft"));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var entry = harness.Published.ShouldHaveSingleItem();
        entry.EntityId.ShouldNotBeNullOrWhiteSpace();

        Deserialize(entry.Changes).ShouldNotContainKey(nameof(AuditedEntity.Id));
    }

    [Test(Description = "Editing only an owned value object records the change on its owner, under the navigation it hangs off")]
    public async Task Should_RecordOwnedValueChange_When_OnlyTheValueObjectIsEdited()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            var entity = new AuditedEntity(execution, "ORD-003", "Draft");
            db.Audited.Add(entity);
            await db.SaveChangesAsync(CancellationToken.None);

            harness.Published.Clear();

            entity.ChangeCity("Second City");
            await db.SaveChangesAsync(CancellationToken.None);
        });

        // The owner stays Unchanged here — without folding owned values in, this save would vanish.
        var entry = harness.Published.ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.Updated);
        entry.EntityType.ShouldBe(nameof(AuditedEntity));

        var changes = Deserialize(entry.Changes);
        changes.ShouldContainKey($"{nameof(AuditedEntity.DeliveryAddress)}.{nameof(Address.City)}");
        changes[$"{nameof(AuditedEntity.DeliveryAddress)}.{nameof(Address.City)}"].Old.ShouldBe("Initial City");
        changes[$"{nameof(AuditedEntity.DeliveryAddress)}.{nameof(Address.City)}"].New.ShouldBe("Second City");
    }

    [Test(Description = "Replacing a value object wholesale still reports what the values were before: an address replaced, not edited, still says where the order used to go")]
    public async Task Should_RecordPreviousOwnedValues_When_TheValueObjectIsReplaced()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            var entity = new AuditedEntity(execution, "ORD-005", "Draft");
            db.Audited.Add(entity);
            await db.SaveChangesAsync(CancellationToken.None);

            harness.Published.Clear();

            entity.ReplaceAddress("Third City");
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var changes = Deserialize(harness.Published.ShouldHaveSingleItem().Changes);
        var city = $"{nameof(AuditedEntity.DeliveryAddress)}.{nameof(Address.City)}";

        changes.ShouldContainKey(city);
        changes[city].New.ShouldBe("Third City");
        changes[city].Old.ShouldBe("Initial City");
    }

    [Test(Description = "A collection property records its contents: a tree path is an int[], and its type name says nothing about a node being moved")]
    public async Task Should_RecordCollectionContents_When_PropertyIsAnArray()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            var entity = new AuditedEntity(execution, "ORD-006", "Draft");
            db.Audited.Add(entity);
            await db.SaveChangesAsync(CancellationToken.None);

            harness.Published.Clear();

            entity.Reparent(execution, [1, 42]);
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var changes = Deserialize(harness.Published.ShouldHaveSingleItem().Changes);

        changes.ShouldContainKey(nameof(AuditedEntity.Path));
        changes[nameof(AuditedEntity.Path)].Old.ShouldBe("[1]");
        changes[nameof(AuditedEntity.Path)].New.ShouldBe("[1, 42]");
    }

    [Test(Description = "A timestamp is recorded in ISO 8601 — the invariant culture writes MM/dd/yyyy, which reads as a different date to most of the people this journal is for")]
    public async Task Should_RecordTimestampsInIso_When_PropertyIsADate()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            var entity = new AuditedEntity(execution, "ORD-008", "Draft");
            db.Audited.Add(entity);
            await db.SaveChangesAsync(CancellationToken.None);

            harness.Published.Clear();

            entity.Resolve(execution, new DateTimeOffset(2026, 7, 28, 20, 55, 44, TimeSpan.Zero));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var changes = Deserialize(harness.Published.ShouldHaveSingleItem().Changes);

        changes[nameof(AuditedEntity.ResolvedAt)].New.ShouldBe("2026-07-28T20:55:44.000+00:00");
    }

    [Test(Description = "Re-assigning a collection with the same contents records nothing: a path rewritten to the same value did not change")]
    public async Task Should_RecordNothing_When_CollectionIsReassignedUnchanged()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            var entity = new AuditedEntity(execution, "ORD-007", "Draft");
            db.Audited.Add(entity);
            await db.SaveChangesAsync(CancellationToken.None);

            harness.Published.Clear();

            // A fresh array with identical contents — a different reference, the same value.
            entity.Reparent(execution, [1]);
            await db.SaveChangesAsync(CancellationToken.None);
        });

        harness.Published.ShouldBeEmpty();
    }

    [Test(Description = "A property created as false is recorded as such — created switched off must not read the same as never set")]
    public async Task Should_RecordFalseValue_When_EntityIsCreatedSwitchedOff()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-004", "Draft"));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var changes = Deserialize(harness.Published.ShouldHaveSingleItem().Changes);
        changes.ShouldContainKey(nameof(AuditedEntity.IsActive));
        changes[nameof(AuditedEntity.IsActive)].New.ShouldBe("False");
    }

    [Test(Description = "A save survives an actor that cannot be resolved, and is filed as system: a scheduled job must not fail because the journal wanted a login")]
    public async Task Should_SaveAndRecordAsSystem_When_ActorCannotBeResolved()
    {
        await using var harness = new AuditHarness(throwingActor: true);

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-009", "Draft"));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var entry = harness.Published.ShouldHaveSingleItem();
        entry.ActorUserId.ShouldBe(Guid.Empty);
        entry.ActorLogin.ShouldBeNull();
    }

    [Test(Description = "Entities without the auditable marker produce no journal entries — coverage is opt-in")]
    public async Task Should_RecordNothing_When_EntityIsNotMarkedAuditable()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Plain.Add(new PlainEntity(execution, "not audited"));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        harness.Published.ShouldBeEmpty();
    }

    [Test(Description = "Updating an entity records only the properties that actually moved, with their previous and new values")]
    public async Task Should_RecordChangedPropertiesOnly_When_EntityIsUpdated()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-002", "Draft"));
            await db.SaveChangesAsync(CancellationToken.None);

            var entity = await db.Audited.SingleAsync(CancellationToken.None);
            entity.ChangeStatus(execution, "Placed");
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var update = harness.Published.Last();
        update.Action.ShouldBe(AuditAction.Updated);

        var changes = Deserialize(update.Changes);
        changes.ShouldContainKey("Status");
        changes["Status"].Old.ShouldBe("Draft");
        changes["Status"].New.ShouldBe("Placed");
        changes.ShouldNotContainKey("Name");
    }

    [Test(Description = "A write that only touches ignored and infrastructure columns records nothing — the journal stays free of entries that say nothing changed")]
    public async Task Should_RecordNothing_When_OnlyIgnoredPropertiesChange()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-003", "Draft"));
            await db.SaveChangesAsync(CancellationToken.None);
            harness.Published.Clear();

            var entity = await db.Audited.SingleAsync(CancellationToken.None);
            entity.TouchSyncMarker(execution);
            await db.SaveChangesAsync(CancellationToken.None);
        });

        harness.Published.ShouldBeEmpty();
    }

    [Test(Description = "System-initiated changes are attributed to the canonical empty actor rather than to a logged-in user")]
    public async Task Should_RecordEmptyActor_When_ChangeIsSystemInitiated()
    {
        await using var harness = new AuditHarness(systemActor: true);

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-004", "Draft"));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var entry = harness.Published.ShouldHaveSingleItem();
        entry.ActorUserId.ShouldBe(Guid.Empty);
        entry.ActorLogin.ShouldBeNull();
        entry.ActorTenantId.ShouldBeNull();
    }

    [Test(Description = "A secret is recorded as having changed, with both values replaced — ignoring it instead would leave an empty diff and erase the event itself, which is the one thing an audit journal must not lose")]
    public async Task Should_RecordRedactedValues_When_SecretPropertyChanges()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-008", "Draft"));
            await db.SaveChangesAsync(CancellationToken.None);
            harness.Published.Clear();

            var entity = await db.Audited.SingleAsync(CancellationToken.None);
            entity.ChangeSecret(execution, "new-secret");
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var changes = Deserialize(harness.Published.ShouldHaveSingleItem().Changes);
        changes.ShouldContainKey("Secret");
        changes["Secret"].Old.ShouldBe(AuditRedactAttribute.Placeholder);
        changes["Secret"].New.ShouldBe(AuditRedactAttribute.Placeholder);
    }

    [Test(Description = "The tenant a row belongs to is recorded as its subject: what makes a change the system made visible to that tenant, though no tenant acted")]
    public async Task Should_RecordSubjectTenant_When_EntityNominatesOne()
    {
        var owner = Guid.NewGuid();
        await using var harness = new AuditHarness(systemActor: true);

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-006", "Draft", owner));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var entry = harness.Published.ShouldHaveSingleItem();
        entry.ActorTenantId.ShouldBeNull();
        entry.SubjectTenantId.ShouldBe(owner);
    }

    [Test(Description = "A reassigned row is subject to its NEW tenant: the entry follows the row, and the previous holder has no claim to what happens under someone else")]
    public async Task Should_RecordNewSubjectTenant_When_RowIsReassigned()
    {
        var previous = Guid.NewGuid();
        var next = Guid.NewGuid();
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-007", "Draft", previous));
            await db.SaveChangesAsync(CancellationToken.None);
            harness.Published.Clear();

            var entity = await db.Audited.SingleAsync(CancellationToken.None);
            entity.Reassign(execution, next);
            await db.SaveChangesAsync(CancellationToken.None);
        });

        harness.Published.ShouldHaveSingleItem().SubjectTenantId.ShouldBe(next);
    }

    [Test(Description = "An entity that nominates no owner records no subject: reference and platform-wide data stays platform-only rather than reaching an arbitrary tenant")]
    public async Task Should_RecordNoSubjectTenant_When_EntityNominatesNone()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Unowned.Add(new UnownedAuditedEntity(execution, "platform-wide"));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        harness.Published.ShouldHaveSingleItem().SubjectTenantId.ShouldBeNull();
    }

    [Test(Description = "Deleting an auditable entity records the action without a diff — the row is gone and the action already carries the meaning")]
    public async Task Should_RecordDeletedWithoutDiff_When_AuditableEntityIsRemoved()
    {
        await using var harness = new AuditHarness();

        await harness.ExecuteAsync(async (db, execution) =>
        {
            db.Audited.Add(new AuditedEntity(execution, "ORD-005", "Draft"));
            await db.SaveChangesAsync(CancellationToken.None);
            harness.Published.Clear();

            db.Audited.Remove(await db.Audited.SingleAsync(CancellationToken.None));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var entry = harness.Published.ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.Deleted);
        Deserialize(entry.Changes).ShouldBeEmpty();
    }

    private static Dictionary<string, RecordedChange> Deserialize(string changes) =>
        JsonSerializer.Deserialize<Dictionary<string, RecordedChange>>(
            changes, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;

    private sealed record RecordedChange(string? Old, string? New);

    // --- Harness ---

    /// <summary>
    /// Minimal host for the interceptor: an InMemory context deriving from <see cref="DbContextBase"/>
    /// (the interceptor reuses its diff), a recording bus and an ambient scope, which is how consumers
    /// and jobs publish theirs in production.
    /// </summary>
    private sealed class AuditHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        public AuditHarness(bool systemActor = false, bool throwingActor = false)
        {
            var timeProvider = new TimeProviderMock(DateTimeOffset.UtcNow);
            var actor = throwingActor
                ? ClaimlessActor()
                : systemActor
                    ? UserContextMockFactory.CreateSystemUser()
                    : UserContextMockFactory.CreateJwtUser(timeProvider);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddAuditPersistence();
            services.AddScoped<IDomainExecutionContext>(_ => new TestDomainExecutionContext(actor, timeProvider));
            services.AddScoped<IMessageBus>(_ => new RecordingMessageBus(Published));
            services.AddDbContext<AuditTestDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase($"audit-{Guid.NewGuid()}");
                options.ApplyAuditInterceptor(sp);
            });

            _provider = services.BuildServiceProvider();
        }

        public List<AuditRecordedV1> Published { get; } = [];

        /// <summary>
        /// Stands in for a Hangfire job or a consumer: no HTTP context, so asking for the user id
        /// throws rather than returning anything.
        /// </summary>
        private static IUserContext ClaimlessActor()
        {
            var actor = new Mock<IUserContext>();
            actor.SetupGet(x => x.IsSystemCall).Returns(false);
            actor.SetupGet(x => x.UserId).Throws(new UnauthorizedAccessException("User ID claim is missing"));
            actor.SetupGet(x => x.Login).Throws(new UnauthorizedAccessException("User ID claim is missing"));
            actor.SetupGet(x => x.TenantId).Returns((Guid?)null);
            return actor.Object;
        }

        public async Task ExecuteAsync(Func<AuditTestDbContext, IDomainExecutionContext, Task> action)
        {
            await using var scope = _provider.CreateAsyncScope();

            // Mirrors production: outside an HTTP request the scope is published ambiently.
            using (DomainEventScopeContext.Use(scope.ServiceProvider))
            {
                var db = scope.ServiceProvider.GetRequiredService<AuditTestDbContext>();
                var execution = scope.ServiceProvider.GetRequiredService<IDomainExecutionContext>();
                await action(db, execution);
            }
        }

        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }

    private sealed class RecordingMessageBus(List<AuditRecordedV1> sink) : IMessageBus
    {
        public Task PublishAsync<T>(T busEvent, CancellationToken ct = default)
            where T : class, IBusEvent
        {
            if (busEvent is AuditRecordedV1 audit) sink.Add(audit);
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T command, CancellationToken ct = default)
            where T : class, IBusCommand => Task.CompletedTask;
    }

    // --- Test entities ---

    [Auditable("Testing",
        DisplayProperty = nameof(Name),
        SubjectTenantProperty = nameof(OwnerTenantId))]
    private sealed class AuditedEntity : Entity<Guid>
    {
        /// <summary>EF Core constructor.</summary>
        private AuditedEntity() { }

        public AuditedEntity(
            IDomainExecutionContext context, string name, string status, Guid? ownerTenantId = null)
        {
            Id = Guid.NewGuid();
            Name = name;
            Status = status;
            OwnerTenantId = ownerTenantId ?? Guid.Empty;
            MarkCreated(context);
        }

        public string Name { get; private set; } = null!;
        public string Status { get; private set; } = null!;

        /// <summary>The tenant the row belongs to: what a change the system made is shown to.</summary>
        public Guid OwnerTenantId { get; private set; }

        public void Reassign(IDomainExecutionContext context, Guid ownerTenantId)
        {
            OwnerTenantId = ownerTenantId;
            MarkUpdated(context);
        }

        /// <summary>Background-sync bookkeeping — moves constantly and carries no operator meaning.</summary>
        [AuditIgnore]
        public int SyncMarker { get; private set; }

        /// <summary>Stands in for a password hash or key material: the change is recorded, the value is not.</summary>
        [AuditRedact]
        public string Secret { get; private set; } = "initial-secret";

        public void ChangeSecret(IDomainExecutionContext context, string secret)
        {
            Secret = secret;
            MarkUpdated(context);
        }

        public void ChangeStatus(IDomainExecutionContext context, string status)
        {
            Status = status;
            MarkUpdated(context);
        }

        public void TouchSyncMarker(IDomainExecutionContext context)
        {
            SyncMarker++;
            MarkUpdated(context);
        }

        /// <summary>Stands in for an order's delivery address: an owned value object.</summary>
        public Address DeliveryAddress { get; private set; } = new();

        /// <summary>
        /// Edits only the owned value — the owner's own columns stay untouched, which is what makes
        /// EF leave it Unchanged.
        /// </summary>
        public void ChangeCity(string city) => DeliveryAddress.Rename(city);

        /// <summary>
        /// Replaces the whole value object, the way services usually update one.
        /// </summary>
        public void ReplaceAddress(string city) => DeliveryAddress = new Address(city);

        /// <summary>Whether the row is switched on — a flag whose <c>false</c> has to survive creation.</summary>
        public bool IsActive { get; private set; }

        /// <summary>Stands in for a materialised tree path: a collection, not a scalar.</summary>
        public int[] Path { get; private set; } = [1];

        /// <summary>Stands in for a resolution timestamp — the shape most likely to be misread.</summary>
        public DateTimeOffset? ResolvedAt { get; private set; }

        public void Resolve(IDomainExecutionContext context, DateTimeOffset resolvedAt)
        {
            ResolvedAt = resolvedAt;
            MarkUpdated(context);
        }

        public void Reparent(IDomainExecutionContext context, int[] path)
        {
            Path = path;
            MarkUpdated(context);
        }
    }

    /// <summary>Owned value object: no identity, no journal row of its own.</summary>
    private sealed class Address
    {
        public Address() { }

        public Address(string city) => City = city;

        public string City { get; private set; } = "Initial City";

        public void Rename(string city) => City = city;
    }

    /// <summary>Auditable, but owned by nobody — reference data, platform-wide settings.</summary>
    [Auditable("Testing", DisplayProperty = nameof(Name))]
    private sealed class UnownedAuditedEntity : Entity<Guid>
    {
        /// <summary>EF Core constructor.</summary>
        private UnownedAuditedEntity() { }

        public UnownedAuditedEntity(IDomainExecutionContext context, string name)
        {
            Id = Guid.NewGuid();
            Name = name;
            MarkCreated(context);
        }

        public string Name { get; private set; } = null!;
    }

    private sealed class PlainEntity : Entity<Guid>
    {
        /// <summary>EF Core constructor.</summary>
        private PlainEntity() { }

        public PlainEntity(IDomainExecutionContext context, string name)
        {
            Id = Guid.NewGuid();
            Name = name;
            MarkCreated(context);
        }

        public string Name { get; private set; } = null!;
    }

    private sealed class AuditTestDbContext(DbContextOptions<AuditTestDbContext> options) : DbContextBase(options)
    {
        public DbSet<AuditedEntity> Audited => Set<AuditedEntity>();
        public DbSet<UnownedAuditedEntity> Unowned => Set<UnownedAuditedEntity>();
        public DbSet<PlainEntity> Plain => Set<PlainEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditedEntity>().HasKey(e => e.Id);
            modelBuilder.Entity<AuditedEntity>().OwnsOne(e => e.DeliveryAddress);

            // Npgsql attaches this to integer[] on its own; the in-memory provider does not, and
            // without it every save reports the array as changed.
            modelBuilder.Entity<AuditedEntity>()
                .Property(e => e.Path)
                .Metadata.SetValueComparer(new ValueComparer<int[]>(
                    (left, right) => left!.SequenceEqual(right!),
                    value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                    value => value.ToArray()));
            modelBuilder.Entity<UnownedAuditedEntity>().HasKey(e => e.Id);
            modelBuilder.Entity<PlainEntity>().HasKey(e => e.Id);
        }
    }
}
