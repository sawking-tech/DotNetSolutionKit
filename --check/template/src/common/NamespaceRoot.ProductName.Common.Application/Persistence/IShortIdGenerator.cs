namespace NamespaceRoot.ProductName.Common.Application.Persistence;

/// <summary>
/// The next number of a database sequence, taken before the entity that carries it is created.
/// </summary>
/// <remarks>
/// For numbers people read and type: an order number, an invoice number, a short customer code. The
/// service takes the number first and passes it to the entity's constructor, so the events the entity
/// raises already carry it. A sequence hands out a value once, even when the transaction that took it
/// rolls back, so two callers never get the same number; gaps are normal and mean nothing.
/// </remarks>
public interface IShortIdGenerator
{
    /// <summary>
    /// The next value of <paramref name="sequenceName"/>, a sequence the service's migrations create,
    /// qualified with its schema (<c>orders.order_number_seq</c>).
    /// </summary>
    Task<long> GetNextAsync(string sequenceName, CancellationToken cancellationToken = default);
}
