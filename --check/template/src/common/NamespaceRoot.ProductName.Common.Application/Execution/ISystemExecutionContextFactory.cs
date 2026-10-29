using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Application.Execution;

/// <summary>
/// A context that acts as the system, for work no person asked for: a scheduled job, a consumer of a
/// platform event, seeding.
/// </summary>
/// <remarks>
/// The <see cref="IDomainExecutionContext"/> a scope resolves is whoever is behind it: the caller of a
/// request, or the person who enqueued a job. A recurring job that changes data on its own account says
/// so explicitly with this context, so the audit trail does not name the last person who happened to
/// trigger it.
/// </remarks>
public interface ISystemExecutionContextFactory
{
    IDomainExecutionContext Create();
}
