using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Domain.Idempotency;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Idempotency;

/// <summary>
/// The idempotency log in a service's own schema, so that it commits in the same transaction as the work.
/// </summary>
/// <typeparam name="TContext">The context owning the service's schema.</typeparam>
public class EntityFrameworkIdempotencyLog<TContext>(TContext context) : IIdempotencyLog
    where TContext : DbContextBase
{
    public Task<IdempotencyRecord?> FindAsync(string scope, string key, CancellationToken cancellationToken = default) =>
        context.Set<IdempotencyRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Scope == scope && record.Key == key, cancellationToken);

    public void Add(IdempotencyRecord record) => context.Set<IdempotencyRecord>().Add(record);
}
