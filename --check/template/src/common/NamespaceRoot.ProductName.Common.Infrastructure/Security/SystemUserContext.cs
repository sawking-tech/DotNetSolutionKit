using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

/// <summary>
/// Production <see cref="IUserContext"/> used by background jobs and other server-initiated flows
/// where no HTTP user is present. Fixed identity, <c>TenantId = null</c> (so platform-only policy
/// checks pass) and <c>AuthContext.Type = System</c> for audit trails.
/// </summary>
public sealed class SystemUserContext : IUserContext
{
    /// <summary>Well-known platform identity for system-initiated work. All-zero Guid.</summary>
    public static readonly Guid SystemUserId = Guid.Empty;

    /// <summary>Singleton — the system identity is immutable and process-wide.</summary>
    public static readonly SystemUserContext Instance = new();

    private SystemUserContext()
    {
        AuthContext = new AuthContext(AuthMethod.System, SystemUserId.ToString(), DateTimeOffset.MaxValue);
    }

    public Guid UserId => SystemUserId;
    public Guid? TenantId => null;
    public string? Login => "system";
    public string? DisplayName => "System";
    public Guid? ApiKeyId => null;
    public IReadOnlyList<string> Roles { get; } = [];
    public IReadOnlyList<string> Permissions { get; } = [];
    public IAuthContext AuthContext { get; }
}
