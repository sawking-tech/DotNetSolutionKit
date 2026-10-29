namespace NamespaceRoot.ProductName.Common.Domain.Context;

/// <summary>
/// User context, unified for all application services.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// User unique identifier
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// The tenant the user acts for; null outside any tenant, as for the platform itself
    /// </summary>
    Guid? TenantId  { get; }

    /// <summary>
    /// User login/email
    /// </summary>
    string? Login { get; }

    /// <summary>
    /// User display name
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// API Key identifier (if authenticated via API Key)
    /// </summary>
    Guid? ApiKeyId { get; }

    /// <summary>
    /// Check if user authenticated via JWT
    /// </summary>
    bool IsJwtAuthenticated => AuthContext.Type == AuthMethod.Jwt;

    /// <summary>
    /// Check if user authenticated via API Key
    /// </summary>
    bool IsApiKeyAuthenticated => AuthContext.Type == AuthMethod.ApiKey;

    /// <summary>
    /// Check if user is system user
    /// </summary>
    bool IsSystemCall => AuthContext.Type == AuthMethod.System;

    /// <summary>
    /// Current authentication context data
    /// </summary>
    IAuthContext AuthContext { get; }
}
