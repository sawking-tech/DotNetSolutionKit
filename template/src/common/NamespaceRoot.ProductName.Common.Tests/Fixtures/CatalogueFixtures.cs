using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Domain;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Domain.Events;
using NamespaceRoot.ProductName.Common.Domain.Persistence;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;
using NamespaceRoot.ProductName.Common.Domain.Querying;

// Shouldly ships a SortDirection of its own, and the global usings bring it into every test file.
using SortDirection = NamespaceRoot.ProductName.Common.Domain.Querying.SortDirection;

namespace NamespaceRoot.ProductName.Common.Tests.Fixtures;

/// <summary>
/// A two-table model standing in for a real service: an aggregate root with a relation to load, and one
/// repository built on the shared base. Deliberately dull - the tests are about the base class, not about
/// this domain.
/// </summary>
public class Region : Entity<Guid>
{
    public Region(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Name { get; private set; }

    public List<Plan> Plans { get; private set; } = [];
}

public class Plan : EventfulEntity<Guid>, IAggregateRoot
{
    public Plan(Guid id, string name, int priceMinor, bool isActive, Region region)
    {
        Id = id;
        Name = name;
        PriceMinor = priceMinor;
        IsActive = isActive;
        Region = region;
        RegionId = region.Id;
    }

    private Plan()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public int PriceMinor { get; private set; }

    public bool IsActive { get; private set; }

    public Guid RegionId { get; private set; }

    public Region Region { get; private set; } = null!;

    /// <summary>
    /// A change that both edits the row and announces itself, so that discarding the work can be
    /// checked to drop the announcement along with the edit.
    /// </summary>
    public void Reprice(int priceMinor, IDomainExecutionContext context)
    {
        PriceMinor = priceMinor;
        AddDomainEvent(new PlanRepriced(context, priceMinor));
    }
}

/// <summary>
/// The one event this stand-in domain raises.
/// </summary>
public sealed record PlanRepriced(IDomainExecutionContext Context, int PriceMinor) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; } = Context.TimeProvider.GetUtcNow();
}

public class CatalogueDbContext : DbContextBase
{
    public CatalogueDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<Region> Regions => Set<Region>();
}

/// <summary>
/// A repository written the way a service is expected to write one: it declares what may be sorted by and
/// adds nothing else.
/// </summary>
public class PlanRepository : EntityFrameworkRepository<Plan, Guid, CatalogueDbContext>
{
    public PlanRepository(CatalogueDbContext context)
        : base(context)
    {
    }

    protected override IReadOnlyDictionary<string, string> SortFields { get; } =
        new Dictionary<string, string>
        {
            ["name"] = nameof(Plan.Name),
            ["price"] = nameof(Plan.PriceMinor),
        };

    protected override string DefaultSortField => nameof(Plan.Name);
}

/// <summary>
/// The request shape a controller would bind: page, size, sort field and direction in one object.
/// </summary>
public sealed record PageRequest : IPaginationRequest, ISortableRequest
{
    public int Page { get; init; }

    public int PageSize { get; init; }

    public string? SortBy { get; init; }

    public SortDirection SortDir { get; init; }
}
