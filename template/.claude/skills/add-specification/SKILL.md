---
name: add-specification
description: Create or review the specifications that filter and search a domain entity of this solution, and the filter extension that composes them.
---

Scaffold or review the specifications of a domain entity.

## Usage
- `/add-specification <EntityName>` - scaffold `<EntityName>Specifications.cs` and its filter extension
- `/add-specification review <file>` - review existing specifications

Why repositories take specifications: [ADR-002](https://dotskit.sawking.tech/docs.html#adr-002).

---

## Filtering is business logic, not repository logic

A repository executes queries. Filter conditions pushed into it (`if (status.HasValue) query = query.Where(...)`)
make it aware of business rules, and they can only be tested by running the query.

A specification is a LINQ expression in the domain layer. It composes with `&` and `|`, it is tested
without EF, and a service test on the in-memory database runs the same expression against the same model.

```
Service layer  ->  composes specifications (business logic)
Repository     ->  runs the composed expression (infrastructure)
```

## Why `ICaseInsensitiveSearch` and never `.ToLower().Contains()`

`ICaseInsensitiveSearch` (`Common/Domain/Specifications`) builds the search condition as an ordinary
specification. The database implementation translates it to `ILIKE` on PostgreSQL, `LIKE` under a
case-insensitive collation on SQL Server:

1. **Performance.** `ILIKE` can use a `pg_trgm` index; `.ToLower().Contains()` cannot use an index on the
   column.
2. **Safety.** The implementation escapes `%`, `_` and the escape character `/`, so user input matches
   literally; a search for `50%` with raw `.Contains()` turns into a pattern.
3. **Testability.** A service test registers `InMemoryCaseInsensitiveSearch`, which answers as the
   database does.
4. **One place.** If the entity has specifications, text search is one more of them.

**Text search always goes through `ICaseInsensitiveSearch`. Never `.ToLower()` or `.Contains()` in a
specification.**

---

## Specifications are mandatory when filtering

| Situation | Specification? |
|-----------|----------------|
| A repository call filters by any field | Yes |
| A service applies `if (filter.X.HasValue)` conditions | Yes - move the conditions into specifications |
| `GetByIdAsync` | No |
| A uniqueness check | No - `AnyAsync` with one specification |

Never pass raw filter values (`bool descending`, `string? status`, `DateTimeOffset? from`) to a repository.
The service builds one `QuerySpecification<T>` and passes it with the request that pages and sorts.

---

## Location

Two files, two layers:

**Specifications (domain).** Atomic predicates; no DTOs, no application types.
```
NamespaceRoot.ProductName.<Service>/Domain/<Area>/Specifications/<EntityName>Specifications.cs
```

**Filter extensions (application).** Turn a filter DTO into a composed specification.
```
NamespaceRoot.ProductName.<Service>.Application/<Area>/Extensions/<EntityName>FilterExtensions.cs
```

Before creating the folders, look at where the service already keeps its entities and follow it.

---

## Template

```csharp
using LinqSpecs;
using NamespaceRoot.ProductName.Common.Domain.Specifications;

namespace NamespaceRoot.ProductName.<Service>.Domain.<Area>.Specifications;

/// <summary>
/// Atomic, composable specifications for querying <see cref="Order"/>.
/// Compose in the application layer with &amp; (AND) and | (OR).
/// </summary>
public static class OrderSpecifications
{
    /// <summary>Orders of one customer.</summary>
    public static Specification<Order> OfCustomer(Guid customerId)
        => new AdHocSpecification<Order>(o => o.CustomerId == customerId);

    /// <summary>Orders in the given status.</summary>
    public static Specification<Order> WithStatus(OrderStatus status)
        => new AdHocSpecification<Order>(o => o.Status == status);

    /// <summary>Placed on or after <paramref name="from"/>, inclusive.</summary>
    public static Specification<Order> PlacedFrom(DateTimeOffset from)
        => new AdHocSpecification<Order>(o => o.PlacedAt >= from);

    /// <summary>Case-insensitive search across the number and the customer's e-mail.</summary>
    public static Specification<Order> MatchSearch(string? term, ICaseInsensitiveSearch search)
    {
        if (string.IsNullOrWhiteSpace(term))
            return new AdHocSpecification<Order>(_ => true);
        var pattern = term.Trim();
        return search.GetSpecification<Order>(o => o.Number, pattern)
             | search.GetSpecification<Order>(o => o.CustomerEmail, pattern);
    }
}
```

`GetSpecification` escapes the wildcards and wraps the term for a "contains" search; trimming is the
caller's, so the specification trims.

---

## Composition in the application layer

The composed specification is built in a filter extension, not in the repository and not as a private
method of the service:

```csharp
public static class OrderFilterExtensions
{
    public static QuerySpecification<Order> ToQuery(this OrderFilter filter, Guid customerId, ICaseInsensitiveSearch search)
    {
        Specification<Order> spec = OrderSpecifications.OfCustomer(customerId);   // the scope first
        if (filter.Status.HasValue)
            spec &= OrderSpecifications.WithStatus(filter.Status.Value);
        if (filter.From.HasValue)
            spec &= OrderSpecifications.PlacedFrom(filter.From.Value);
        spec &= OrderSpecifications.MatchSearch(filter.Search, search);
        return new QuerySpecification<Order>(spec);
    }
}

// The service: one line builds the query, one line runs it
var page = await orders.ListPageAsync(filter.ToQuery(customerId, search).Include(o => o.Lines), filter, ct);
```

**Rules:**
- One specification per atomic business condition, named for its meaning.
- `&` for AND, `|` for OR (LinqSpecs operators).
- "All rows" is `new AdHocSpecification<T>(_ => true)`, never `null`.
- Scope and access specifications come first; filters are ANDed on top.
- Relations to load are declared on the query with `.Include(o => o.Lines)`, not in the repository.

---

## Review flags

| Pattern | Issue |
|---------|-------|
| A repository method with `bool descending`, `string? status`, `DateTimeOffset? from` | Replace with a specification built in a filter extension |
| `.Where(e => e.X == value)` in a repository body | Move into a specification |
| `.ToLower().Contains()` in a specification or repository | Use `ICaseInsensitiveSearch` |
| A specification returned as `null` for "no filter" | Return `new AdHocSpecification<T>(_ => true)` |
| Scope and business filters mixed in one specification method | Split: one method per concern |
| A private `BuildSpec(...)` in the service class | Move to `<EntityName>FilterExtensions` |
| A filter extension in the domain layer | It knows the DTO: application layer |

## Next

The repository that runs the query: `/add-repository`.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
