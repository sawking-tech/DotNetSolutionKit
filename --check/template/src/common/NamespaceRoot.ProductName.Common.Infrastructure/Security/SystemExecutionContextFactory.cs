using NamespaceRoot.ProductName.Common.Application.Execution;
using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

/// <inheritdoc cref="ISystemExecutionContextFactory"/>
public sealed class SystemExecutionContextFactory(TimeProvider timeProvider) : ISystemExecutionContextFactory
{
    public IDomainExecutionContext Create() => new ApplicationExecutionContext(SystemUserContext.Instance, timeProvider);
}
