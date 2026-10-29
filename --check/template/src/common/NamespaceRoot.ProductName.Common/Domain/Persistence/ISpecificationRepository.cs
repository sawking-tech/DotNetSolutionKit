using NamespaceRoot.ProductName.Common.Domain.Specifications;
using NamespaceRoot.ProductName.Common.Domain.Querying;

namespace NamespaceRoot.ProductName.Common.Domain.Persistence;

/// <summary>
/// Storage for one aggregate root, addressed by specifications rather than by a method per query.
///
/// The rule this interface exists to enforce: a repository does not grow
/// <c>GetPagedByCustomerAsync(customerId, page, pageSize)</c>. The condition is a specification, the page is
/// an <see cref="IPaginationRequest"/>, and both travel through the same call. A repository adds a method
/// of its own only where a query cannot be stated as a specification at all - aggregate counts and upserts.
/// </summary>
/// <typeparam name="TEntity">The aggregate root this repository stores.</typeparam>
/// <typeparam name="TId">The type of the aggregate's identifier.</typeparam>
public interface ISpecificationRepository<TEntity, in TId>
    where TEntity : Entity<TId>, IAggregateRoot
{
    /// <summary>
    /// Reads one aggregate by its identifier, without loading any relation.
    /// </summary>
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the single aggregate the query selects, or <c>null</c> when it selects none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The query selected more than one aggregate.</exception>
    Task<TEntity?> SingleOrDefaultAsync(
        QuerySpecification<TEntity> query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every aggregate the query selects. Use the paged overload for anything a user can grow.
    /// </summary>
    Task<IReadOnlyList<TEntity>> ListAsync(
        QuerySpecification<TEntity> query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one page of the aggregates the query selects, sorted as the request asks.
    /// </summary>
    /// <remarks>
    /// This is the only paged path. Sort field names come from the request and are resolved against the
    /// mapping the concrete repository declares, so a caller cannot order by a column that is not meant to
    /// be sortable.
    /// </remarks>
    Task<PagedResult<TEntity>> ListPageAsync<TRequest>(
        QuerySpecification<TEntity> query,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IPaginationRequest, ISortableRequest;

    /// <summary>
    /// Counts the aggregates the query selects.
    /// </summary>
    Task<int> CountAsync(QuerySpecification<TEntity> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers whether the query selects anything, without reading it.
    /// </summary>
    Task<bool> AnyAsync(QuerySpecification<TEntity> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks a new aggregate. It reaches storage when the unit of work is saved.
    /// </summary>
    void Add(TEntity entity);

    /// <summary>
    /// Marks an aggregate for deletion. It leaves storage when the unit of work is saved.
    /// </summary>
    void Remove(TEntity entity);
}
