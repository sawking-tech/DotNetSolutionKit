---
name: add-repository
description: Scaffold or review a repository of this solution on EntityFrameworkRepository - specifications, an allow-list of sort fields, aggregate boundaries.
---

Scaffold or review a repository.

## Usage
- `/add-repository <EntityName>` - scaffold the interface and the implementation
- `/add-repository review <file>` - review an existing repository

Ask the user (in their language): is `<EntityName>` an aggregate root or a child entity?

Why specifications: [ADR-002](https://dotskit.sawking.tech/docs.html#adr-002); the shared base:
[persistence](https://dotskit.sawking.tech/docs.html#persistence).

---

## Core principle

**A repository runs queries. It does not filter by business rules and does not sort by them.**

Filtering, paging, sorting and includes are written once, in `EntityFrameworkRepository`, and tested once,
in `Common.Tests`. A concrete repository adds only what is its own: the sort fields it allows, and a query
the shared base cannot express.

```
Service      ->  builds a QuerySpecification (filter extension, see /add-specification)
Repository   ->  runs it: ListPageAsync, ListAsync, SingleOrDefaultAsync, CountAsync, AnyAsync
```

---

## Aggregate root repository

### Interface (domain)

```csharp
/// <summary>Repository of <see cref="Order"/> aggregates.</summary>
public interface IOrderRepository : ISpecificationRepository<Order, Guid>;
```

`ISpecificationRepository<TEntity, TId>` (`Common/Domain/Persistence`) gives `GetByIdAsync`,
`SingleOrDefaultAsync`, `ListAsync`, `ListPageAsync`, `CountAsync`, `AnyAsync`, `Add` and `Remove`. It
accepts only aggregate roots (`IAggregateRoot`). Add a method to the interface only for a query the
specification cannot express.

### Implementation (infrastructure)

```csharp
public sealed class OrderRepository(OrdersDbContext context)
    : EntityFrameworkRepository<Order, Guid, OrdersDbContext>(context), IOrderRepository
{
    // The allow-list: the name a caller sorts by -> the property it orders by. The names are the JSON
    // fields of the response item the client sees, not the filter's fields.
    protected override IReadOnlyDictionary<string, string> SortFields { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["number"] = nameof(Order.Number),
            ["placedAt"] = nameof(Order.PlacedAt),
            ["customer"] = "Customer.Name",          // a path EF can translate
        };

    protected override string DefaultSortField => nameof(Order.PlacedAt);
}
```

Register it in the service's Infrastructure `DependencyInjection.cs`:
`services.AddScoped<IOrderRepository, OrderRepository>();`

### Sorting and paging

- `ListPageAsync(query, request, ct)` counts, sorts, pages and returns `PagedResult<T>` (`Items`,
  `TotalCount`, `Page`, `PageSize`, `TotalPages`) in one call; the request implements `IPaginationRequest`
  and `ISortableRequest`.
- A sort field not in `SortFields` is refused with 400 naming the allowed fields: the map is the
  allow-list, so a caller cannot order by a column the repository never exposed.
- With no sort field, `DefaultSortField` orders the rows; paging without a total order returns arbitrary
  rows per page, so there always is one.
- Page and page size are checked before the action (422 outside 1..1000 or the request's
  `[PaginationLimit]`): [validation and pagination](https://dotskit.sawking.tech/docs.html#validation).

**Never** hand-roll `Skip`/`Take` or `OrderBy` in a repository; `ListPageAsync` does it once for all.

---

## Child entity - no repository of its own for writes

Child entities are created and changed through their aggregate root only. `ISpecificationRepository`
takes aggregate roots only, so a child cannot get `Add` or `Remove` from it.

```csharp
// Wrong: a child changed through a repository of its own
lineRepository.Add(new OrderLine(...));

// Right: through the aggregate
order.AddLine(productId, quantity, price, context);
await unitOfWork.CommitTransactionAsync(ct);
```

A child that has to be queried on its own (a report, a lookup) gets a read-only port with a comment that
says so:

```csharp
/// <summary>
/// Read-only queries over <see cref="OrderLine"/>. Lines are created and changed only through
/// <see cref="Order"/>; this port exists for reading.
/// </summary>
public interface IOrderLineQueries
{
    Task<IReadOnlyList<OrderLine>> ListOfProductAsync(Guid productId, CancellationToken ct = default);
}
```

---

## Loading

- Relations come from the query: `new QuerySpecification<Order>(spec).Include(o => o.Lines)`. Include
  what the use case reads, nothing more.
- `GetByIdAsync` and the list methods return tracked entities, so a use case that loads and changes an
  aggregate saves it without attaching it again.
- A read that returns many rows only to map them to a response, with no change, may use a dedicated query
  with `AsNoTracking()` and a `Select` to the response shape; it lives in the concrete repository.

---

## Review flags

| Pattern | Issue |
|---------|-------|
| `Add` / `Remove` for a child entity | Aggregate boundary violation: change it through the root |
| Filter values as method parameters (`status?`, `from?`, `to?`) | Use a specification, see `/add-specification` |
| `.Where(e => e.X == value)` in a repository body | Move into a specification |
| `.ToLower().Contains()` | Use `ICaseInsensitiveSearch` in a specification |
| `Include` chains in repository methods | Declare them on the `QuerySpecification` |
| Hand-rolled `.Skip().Take()` or `.OrderBy()` | `ListPageAsync` |
| A sort key that is not a field of the response item | The keys are what the client sees |
| A child read port without a comment saying writes go through the root | Add it |

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
