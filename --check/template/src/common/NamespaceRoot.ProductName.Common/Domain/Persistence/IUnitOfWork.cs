namespace NamespaceRoot.ProductName.Common.Domain.Persistence;

/// <summary>
/// Represents the Unit of Work pattern to manage database transactions
/// and coordinate the saving of changes across multiple repositories.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves all changes made in this unit of work to the database.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a new database transaction.
    /// Throws an exception if a transaction is already in progress.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the current transaction and persists changes to the database.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards all changes made within the current transaction.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a value indicating whether there is an active transaction in progress.
    /// </summary>
    bool HasActiveTransaction { get; }

    /// <summary>
    /// Checks if there are any pending changes that have not yet been saved to the database.
    /// </summary>
    /// <returns>True if there are pending changes; otherwise, false.</returns>
    bool HasPendingChanges();

    /// <summary>
    /// Gets the total number of pending changes (added, modified, or deleted entities).
    /// </summary>
    /// <returns>The count of tracked changes.</returns>
    int GetPendingChangesCount();

    /// <summary>
    /// Reverts all tracked changes in memory, resetting entities to their original state.
    /// </summary>
    void DiscardChanges();

    /// <summary>
    /// Detects changes for a specific entity compared to its original state.
    /// </summary>
    /// <param name="entity">The entity instance to inspect.</param>
    /// <param name="isNewEntity">True if the entity is being tracked as a new entry.</param>
    /// <returns>
    /// A dictionary where keys are property names and values contain the property type,
    /// original value, and current value.
    /// </returns>
    Dictionary<string, (Type Type, object? OriginalValue, object? CurrentValue)> GetChangesFor(
        object entity,
        bool isNewEntity = false);
}
