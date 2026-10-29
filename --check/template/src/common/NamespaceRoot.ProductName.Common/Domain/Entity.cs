using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Domain
{
    /// <summary>
    /// Base entity representation
    /// </summary>
    public abstract class Entity
    {
        /// <summary>
        /// Date and time when the entity was created (UTC)
        /// </summary>
        public DateTimeOffset CreatedAt { get; private set; }

        /// <summary>
        /// Date and time when the entity was last updated (UTC)
        /// </summary>
        public DateTimeOffset UpdatedAt { get; private set; }

        protected void MarkCreated(IDomainExecutionContext context)
        {
            var now = context.TimeProvider.GetUtcNow();
            CreatedAt = now;
            UpdatedAt = now;
        }

        protected void MarkUpdated(IDomainExecutionContext context)
        {
            UpdatedAt = context.TimeProvider.GetUtcNow();
        }
    }

    /// <summary>
    /// Entity with identifier
    /// </summary>
    /// <typeparam name="TId">Identifier type</typeparam>
    public abstract class Entity<TId> : Entity
    {
        /// <summary>
        /// Entity identifier
        /// </summary>
        public TId Id { get; protected set; } = default!;
    }
}
