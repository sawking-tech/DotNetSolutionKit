namespace NamespaceRoot.ProductName.Common.Domain.Context;

/// <summary>
/// Execution context for any action in domain
/// </summary>
public interface IDomainExecutionContext
{
    /// <summary>
    /// Actor performing the action
    /// </summary>
    IUserContext Actor { get; }

    /// <summary>
    /// Current date, time
    /// </summary>
    TimeProvider TimeProvider { get; }
}
