// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Application.Extensions;
using NamespaceRoot.ProductName.Common.Domain;
using NamespaceRoot.ProductName.Common.Domain.Persistence;
using NamespaceRoot.ProductName.Common.Domain.Specifications;
using NamespaceRoot.ProductName.Common.Domain.Querying;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;

/// <summary>
/// Entity Framework implementation of <see cref="ISpecificationRepository{TEntity,TId}"/>.
///
/// A concrete repository derives from this and declares which fields it allows sorting by. It does not
/// re-implement filtering or paging: both arrive through the specification and the request, which is what
/// keeps every repository in the platform answering the same way.
/// </summary>
/// <typeparam name="TEntity">The aggregate root this repository stores.</typeparam>
/// <typeparam name="TId">The type of the aggregate's identifier.</typeparam>
/// <typeparam name="TContext">The context owning the aggregate's table.</typeparam>
public abstract class EntityFrameworkRepository<TEntity, TId, TContext>
    : ISpecificationRepository<TEntity, TId>
    where TEntity : Entity<TId>, IAggregateRoot
    where TContext : DbContextBase
{
    protected EntityFrameworkRepository(TContext context)
    {
        Context = context;
    }

    protected TContext Context { get; }

    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    /// <summary>
    /// Sort field names a caller may ask for, mapped to the properties they order by.
    /// </summary>
    /// <remarks>
    /// The mapping is the allow-list. A field that is not in it is refused with
    /// <see cref="NamespaceRoot.ProductName.Common.Exceptions.BadRequestException"/>, which names the
    /// allowed fields, so a caller cannot order by a column the repository never meant to expose.
    /// <see cref="DefaultSortField"/> applies only when the request names no field.
    /// </remarks>
    protected abstract IReadOnlyDictionary<string, string> SortFields { get; }

    /// <summary>
    /// The property rows are ordered by when the request names none.
    /// </summary>
    /// <remarks>
    /// Paging without a total order returns arbitrary rows per page, so there is always one.
    /// </remarks>
    protected virtual string DefaultSortField => nameof(Entity<TId>.Id);

    public virtual async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        await Set.FindAsync([id], cancellationToken);

    public virtual Task<TEntity?> SingleOrDefaultAsync(
        QuerySpecification<TEntity> query,
        CancellationToken cancellationToken = default) =>
        Apply(query).SingleOrDefaultAsync(cancellationToken);

    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(
        QuerySpecification<TEntity> query,
        CancellationToken cancellationToken = default) =>
        await Apply(query).ToListAsync(cancellationToken);

    public virtual async Task<PagedResult<TEntity>> ListPageAsync<TRequest>(
        QuerySpecification<TEntity> query,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IPaginationRequest, ISortableRequest
    {
        var page = request.NormalisedPage();
        var pageSize = request.NormalisedPageSize();

        var selected = Apply(query);
        var totalCount = await selected.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return PagedResult<TEntity>.Empty(page, pageSize);
        }

        var items = await selected
            .ApplySorting(request, SortFields, DefaultSortField)
            .ApplyPagination(request)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, totalCount, page, pageSize);
    }

    public virtual Task<int> CountAsync(
        QuerySpecification<TEntity> query,
        CancellationToken cancellationToken = default) =>
        Apply(query, withIncludes: false).CountAsync(cancellationToken);

    public virtual Task<bool> AnyAsync(
        QuerySpecification<TEntity> query,
        CancellationToken cancellationToken = default) =>
        Apply(query, withIncludes: false).AnyAsync(cancellationToken);

    public virtual void Add(TEntity entity) => Set.Add(entity);

    public virtual void Remove(TEntity entity) => Set.Remove(entity);

    /// <summary>
    /// Turns a specification into a query: the criterion becomes the filter, the declared relations become
    /// joins.
    /// </summary>
    /// <param name="query">The specification to translate.</param>
    /// <param name="withIncludes">
    /// Counting does not need the related data, and loading it would make the database do work nobody
    /// reads.
    /// </param>
    protected IQueryable<TEntity> Apply(QuerySpecification<TEntity> query, bool withIncludes = true)
    {
        var selected = Set.Where(query.ToExpression());
        if (!withIncludes)
        {
            return selected;
        }

        return query.IncludePaths.Aggregate(selected, (current, path) => current.Include(path));
    }
}
