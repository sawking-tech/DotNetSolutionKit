using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework.DataSeeding;

public class SystemSeedContext : IDomainExecutionContext
{
    private static readonly Guid SystemUserId = Guid.Empty;
    private static readonly string SystemUserName = "SystemSeeder";

    public IUserContext Actor { get; }
    public TimeProvider TimeProvider { get; }

    public SystemSeedContext()
    {
        TimeProvider = TimeProvider.System;
        Actor = new SystemUserContext();
    }

    private class SystemUserContext : IUserContext
    {
        public Guid UserId => SystemUserId;
        public Guid? TenantId => null;
        public string Login => SystemUserName;
        public string DisplayName => SystemUserName;
        public Guid? ApiKeyId => null;
        public IAuthContext AuthContext { get; } = new SystemAuthContext();
        public bool IsJwtAuthenticated => false;
        public bool IsApiKeyAuthenticated => false;
        public bool IsSystemCall => true;
    }

    private class SystemAuthContext : IAuthContext
    {
        public AuthMethod Type => AuthMethod.System;
        public string Id => "SystemSeeder";
        public DateTimeOffset ExpireAt => DateTimeOffset.MaxValue;
    }
}
