namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

/// <summary>
/// Standard HTTP header names used for authentication and user context
/// </summary>
public static class AuthHeaders
{
    // Authentication type and validation
    public const string Authorization = "Authorization";
    public const string ApiKey = "X-API-Key";
    public const string AuthType = "X-Auth-Type";
    public const string AuthId = "X-Auth-Id";
    public const string AuthValidated = "X-Auth-Validated";
    public const string AuthExp = "X-Auth-Exp";
    public const string SystemCall = "X-System-Call";

    // User information
    public const string UserId = "X-User-Id";
    public const string UserLogin = "X-User-Login";
    public const string UserDisplayName = "X-User-DisplayName";
    public const string UserRoles = "X-User-Roles";
    public const string UserPermissions = "X-User-Permissions";
    public const string TenantId = "X-Tenant-Id";

    /// <summary>
    /// The headers that carry who the caller is. A service trusts them only next to the internal API
    /// key, and a gateway removes any a client sent before it sets its own.
    /// </summary>
    public static readonly IReadOnlyList<string> UserContext =
    [
        AuthType, AuthId, AuthValidated, AuthExp, SystemCall,
        UserId, UserLogin, UserDisplayName, UserRoles, UserPermissions, TenantId,
    ];
}

/// <summary>
/// Standard claim types used in the application
/// Uses short, readable names optimized for JWT tokens
/// </summary>
public static class AuthClaims
{
    public const string SystemCall = "is_system_call";
    public const string UserId = "user_id";
    public const string UserLogin = "user_login";
    public const string UserRole = "role";
    public const string Email = "email";
    public const string Permissions = "permissions";

    // Application specific claims
    public const string DisplayName = "display_name";
    public const string TenantId = "tenant_id";
    public const string ApiKeyId = "api_key_id";

    // Authentication context claims
    public const string AuthType = "auth_type";
    public const string AuthId = "auth_id";
    public const string AuthExp = "auth_exp";
    public const string AuthValidated = "auth_validated";

    // JWT specific claims
    public const string Jti = "jti";
    public const string Exp = "exp";
    public const string Iat = "iat";
}
