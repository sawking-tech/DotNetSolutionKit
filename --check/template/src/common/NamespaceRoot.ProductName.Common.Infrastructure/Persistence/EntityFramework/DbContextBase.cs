    using NamespaceRoot.ProductName.Common.Domain;
    using NamespaceRoot.ProductName.Common.Domain.Persistence;
    using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
    using Microsoft.EntityFrameworkCore.Storage;

    using NamespaceRoot.ProductName.Common.Domain.Events;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;

    public abstract class DbContextBase : DbContext, IUnitOfWork
    {
        private IDbContextTransaction? _currentTransaction;

        protected DbContextBase(DbContextOptions options)
            : base(options)
        {
        }

        #region IUnitOfWork Implementation

        // ReSharper disable once RedundantOverriddenMember
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                // A row changed by someone else since it was read: the caller reloads and tries again,
                // which a 409 says. Left as it is, the EF exception reaches the client as a 500.
                throw new ConcurrencyException(ConcurrencyMessage, exception);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // A refused insert reaches the caller as the refusal it is, naming the field, instead
                // of whatever the database driver says at five hundred. The idempotency log leans on
                // this too: a replayed request collides on its key and is answered from the record.
                throw new UniqueViolationException(UniqueViolationMessage, exception);
            }
        }

        private const string UniqueViolationMessage = "A record with these values already exists.";

        private const string ConcurrencyMessage = "The row was changed by another write since it was read.";

        private static bool IsUniqueViolation(DbUpdateException exception) =>
            DatabaseProvider.IsUniqueViolation(exception);

        public Dictionary<string, (Type Type, object? OriginalValue, object? CurrentValue)> GetChangesFor(
            object entity, bool isNewEntity = false)
        {
            var trackedEntry = ChangeTracker.Entries().FirstOrDefault(x => x.Entity == entity);

            if (trackedEntry == null)
            {
                throw new ArgumentException("Entity is not being tracked by this context", nameof(entity));
            }

            var result = new Dictionary<string, (Type, object?, object?)>();

            foreach (var property in trackedEntry.Properties.OrderBy(p => p.Metadata.Name))
            {
                if (property.Metadata.IsShadowProperty())
                    continue;

                var original = property.OriginalValue;
                var current = property.CurrentValue;

                if (isNewEntity)
                {
                    if (current is null ||
                        current is false ||
                        (current is string s && string.IsNullOrEmpty(s)) ||
                        (current is DateTimeOffset dto && dto == default))
                    {
                        continue;
                    }
                    result[property.Metadata.Name] = (property.Metadata.ClrType, null, current);
                }
                else
                {
                    if (!Equals(original, current))
                    {
                        result[property.Metadata.Name] = (property.Metadata.ClrType, original, current);
                    }
                }
            }

            return result;
        }

        #endregion

        #region Transaction Management

        /// <summary>
        /// Starts a new transaction.
        /// </summary>
        public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_currentTransaction != null)
            {
                throw new InvalidOperationException("A transaction is already in progress");
            }

            _currentTransaction = await Database.BeginTransactionAsync(cancellationToken);
        }

        /// <summary>
        /// Commits the current transaction.
        /// </summary>
        public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_currentTransaction == null)
            {
                throw new InvalidOperationException("No transaction to commit");
            }

            try
            {
                await SaveChangesAsync(cancellationToken);
                await _currentTransaction.CommitAsync(cancellationToken);
                await DisposeTransactionAsync();
            }
            catch
            {
                await RollbackTransactionAsync(cancellationToken);
                throw;
            }

            // A relational commit has run the post-commit phase through the transaction interceptor. A
            // provider without relational transactions, as the in-memory one of the service tests, fires
            // no transaction event, so the phase runs here.
            if (!Database.IsRelational())
                await DomainEventCompletion.CommittedAsync(this, cancellationToken);
        }

        /// <summary>
        /// Rolls back the current transaction.
        /// </summary>
        public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_currentTransaction == null)
                return;

            try
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
            }
            finally
            {
                await DisposeTransactionAsync();
            }

            if (!Database.IsRelational())
                await DomainEventCompletion.RolledBackAsync(this, cancellationToken);
        }

        public bool HasActiveTransaction => _currentTransaction != null;

        /// <summary>
        /// Gets the current transaction if exists.
        /// </summary>
        public IDbContextTransaction? GetCurrentTransaction() => _currentTransaction;
        #endregion

        #region Helper Methods

        public bool HasPendingChanges() => ChangeTracker.HasChanges();

        public int GetPendingChangesCount() => ChangeTracker.Entries()
            .Count(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

        public void DiscardChanges()
        {
            var entries = ChangeTracker
                .Entries<Entity>()
                .ToList();

            foreach (var entry in entries)
            {
                // 1. Clear domain events to prevent them from firing in future saves
                if (entry.Entity is IHasDomainEvents entityWithEvents)
                {
                    entityWithEvents.ClearDomainEvents();
                }

                // 2. Reset entity state
                entry.State = entry.State switch
                {
                    EntityState.Added => EntityState.Detached,
                    EntityState.Modified or EntityState.Deleted => EntityState.Unchanged,
                    _ => entry.State
                };
            }
        }

        #endregion

        #region Dispose Management

        public new void Dispose()
        {
            DisposeTransaction();
            base.Dispose();
        }

        public new async ValueTask DisposeAsync()
        {
            await DisposeTransactionAsync();
            await base.DisposeAsync();
        }

        private void DisposeTransaction()
        {
            _currentTransaction?.Dispose();
            _currentTransaction = null;
        }

        private async ValueTask DisposeTransactionAsync()
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }

        #endregion
    }
