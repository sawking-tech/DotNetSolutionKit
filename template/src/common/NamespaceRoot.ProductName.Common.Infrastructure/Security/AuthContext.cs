using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

public class AuthContext : IAuthContext
{
    public AuthMethod Type { get; }
    public string Id { get; }
    public DateTimeOffset ExpireAt { get; }

    public AuthContext(AuthMethod type, string id, DateTimeOffset expireAt)
    {
        Type = type;
        Id = id;
        ExpireAt = expireAt;
    }
}
