using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Domain.Idempotency;

/// <summary>
/// The record that a request with a given key has already been carried out, and what it answered.
/// </summary>
/// <remarks>
/// Written in the same transaction as the work itself. Committed separately, an interruption between the
/// two leaves either work nobody can find by its key, or a key claiming work that never happened.
/// </remarks>
public class IdempotencyRecord : Entity<Guid>, IAggregateRoot
{
    // For the ORM.
    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(
        IDomainExecutionContext context,
        string scope,
        string key,
        string operation,
        string response)
    {
        Id = Guid.NewGuid();
        Scope = scope;
        Key = key;
        Operation = operation;
        Response = response;
        MarkCreated(context);
    }

    /// <summary>
    /// Whose key it is: the tenant the actor acts for, or the account.
    /// </summary>
    /// <remarks>
    /// Scoped rather than global: two clients may pick the same key, and one must not be handed the
    /// other's answer.
    /// </remarks>
    public string Scope { get; private set; } = string.Empty;

    /// <summary>The key the client supplied.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>
    /// What was being done. A repeat under the same key for another operation is refused, not answered
    /// with this one's result.
    /// </summary>
    public string Operation { get; private set; } = string.Empty;

    /// <summary>The answer the first attempt produced, serialised; empty for work whose answer is not kept.</summary>
    public string Response { get; private set; } = string.Empty;
}
