# ADR-002: Repositories take queries as specifications

**Status:** Accepted, 2026-01-13 in the first product built on the template; adopted by the template
2026-10-03

**Author:** Vladimir Savkin, the creator of DotNetSolutionKit

A repository interface in a generated service derives from `ISpecificationRepository<TEntity, TId>`
(`Common/Domain/Persistence`). The implementation derives from the abstract
`EntityFrameworkRepository<TEntity, TId, TContext>` (`Common.Infrastructure/Persistence/EntityFramework`).
A query is passed in as a `QuerySpecification<TEntity>`; the repository does not grow a method per query.

## Context

A repository that answers each question with a named method grows one method per question.
`GetByCustomerAsync` is followed by `GetByCustomerAndStatusAsync`, then by a paged variant, then by one that
also sorts. Three problems follow:

- paging is written again inside each method, and `Skip`/`Take` end up in a dozen places, one of them with
  an off-by-one;
- filter conditions are copied between methods that share most of a predicate, so changing a rule means
  finding every copy;
- what gets eagerly loaded is decided per method, so the same entity comes back with different graphs
  depending on which method read it.

Returning `IQueryable` from the repository avoids the method growth, but lets persistence concerns into the
application layer, and a handler's query can then only be tested against a database.

## Decision

- Conditions are `LinqSpecs` specifications, combined with `&`, `|` and `!`.
- Paging goes through one method, `ListPageAsync`, which takes an `IPaginationRequest`. A method like
  `GetPagedByCustomerAsync(customerId, page, pageSize)` does not exist: the caller passes a specification and
  the request.
- Sorting goes through `ISortableRequest`. The concrete repository declares the fields a caller may sort by
  (`SortFields`); any other field is refused with 400 and the list of allowed fields.
- Related data to load is declared in the specification with `Include`, so the shape of the result belongs
  to the query.
- One repository per aggregate root. Entities inside the aggregate are reached through the root.
- A repository adds its own method only for a query that a specification cannot express, such as an
  aggregate count or an upsert.

## What the base class is for

`EntityFrameworkRepository` implements filtering, paging, sorting and includes once. A concrete repository
holds its sort field map and the methods that do not fit a specification. Without the base, each repository
copies the same `GetBySpecificationAsync` and paging code.

The base does not hide EF Core. `Context` and `Set` are available to the derived class, and the
infrastructure layer uses EF Core directly where it needs to. The repository and `IUnitOfWork` exist to keep
the application layer free of persistence code: the application layer states what to read, and the
transaction boundary is `IUnitOfWork`.

## Risks and how they are handled

| Risk | What happens | Handled by |
|---|---|---|
| A query cannot be stated as a specification | A repository grows a method of its own | Allowed for what a specification cannot express, such as an aggregate count or an upsert; anything else is a specification |
| A repository bypasses the base | Paging, sorting or filtering are written again inside it | `Skip(`, `Take(` or `Where(` in a repository body is visible in review and in a search |
| The sort field allow-list is forgotten | A caller cannot sort by a field it needs | The caller gets 400 with the list of allowed fields, so the gap shows on the first try |

## Consequences

A specification is a small class that can be tested without a database: given a list of entities, it selects
what it should and nothing else. A selection rule lives in one file.

The rule can be checked with a search: `Skip(`, `Take(` or `Where(` in the body of a repository
implementation means a query bypassed the specification.

The cost is more classes: a condition that a one-off `Where` would express inline gets its own
specification.

The behaviour of the base is covered by `EntityFrameworkRepositoryTests`, `QuerySpecificationTests` and
`QueryableExtensionsSortingTests` in `Common.Tests`. They run on the EF Core in-memory provider; the paging
and sorting they check do not depend on the database.
