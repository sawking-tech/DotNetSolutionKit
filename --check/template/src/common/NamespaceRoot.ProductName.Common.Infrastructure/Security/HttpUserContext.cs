using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

public sealed class HttpUserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private IAuthContext? _cachedAuthContext;
    private Guid? _cachedUserId;
    private Guid? _cachedTenantId;
    private Guid? _cachedApiKeyId;
    private string? _cachedLogin;
    private string? _cachedDisplayName;

    public Guid UserId => _cachedUserId ??= GetUserId();
    public Guid? TenantId => _cachedTenantId ??= GetTenantId();
    public string? Login => _cachedLogin ??= GetLogin();
    public string? DisplayName => _cachedDisplayName ??= GetDisplayName();
    public Guid? ApiKeyId => _cachedApiKeyId ??= GetApiKeyId();
    public IAuthContext AuthContext => _cachedAuthContext ??= CreateAuthContext();

    public bool IsJwtAuthenticated => AuthContext.Type == AuthMethod.Jwt;
    public bool IsApiKeyAuthenticated => AuthContext.Type == AuthMethod.ApiKey;
    public bool IsSystemCall => AuthContext.Type == AuthMethod.System;

    public HttpUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal Principal => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    private Guid GetUserId()
    {
        var userIdStr = Principal.FindFirstValue(AuthClaims.UserId);

        if (string.IsNullOrWhiteSpace(userIdStr))
            throw new UnauthorizedAccessException("User ID claim is missing");

        if (!Guid.TryParse(userIdStr, out var userId))
            throw new UnauthorizedAccessException($"Invalid user ID format: '{userIdStr}'");

        if (IsSystemCall || userId == Guid.Empty)
            throw new InvalidOperationException("System context has no user ID");

        return userId;
    }

    private Guid? GetTenantId()
    {
        var tenantIdStr = Principal.FindFirstValue(AuthClaims.TenantId);

        if (string.IsNullOrWhiteSpace(tenantIdStr))
            return null;

        if (!Guid.TryParse(tenantIdStr, out var tenantId))
            throw new UnauthorizedAccessException($"Invalid tenant ID format: '{tenantIdStr}'");

        if (IsSystemCall)
            throw new InvalidOperationException("System context has no tenant ID");

        return tenantId;
    }

    private Guid? GetApiKeyId()
    {
        var apiKeyIdStr = Principal.FindFirstValue(AuthClaims.ApiKeyId);
        if (string.IsNullOrWhiteSpace(apiKeyIdStr))
            return null;

        return Guid.TryParse(apiKeyIdStr, out var guid) ? guid : null;
    }

    private string? GetLogin() => Principal.FindFirstValue(AuthClaims.UserLogin);
    private string? GetDisplayName() => Principal.FindFirstValue(AuthClaims.DisplayName);

    private IAuthContext CreateAuthContext()
    {
        var authType = GetAuthMethod(Principal);
        var authId = GetAuthId(Principal);
        var expireAt = GetExpireAt(Principal);

        return new AuthContext(authType, authId, expireAt);
    }

    private AuthMethod GetAuthMethod(ClaimsPrincipal principal)
    {
        var authTypeClaim = principal.FindFirstValue(AuthClaims.AuthType);
        return Enum.TryParse<AuthMethod>(authTypeClaim, ignoreCase: true, out var authMethod)
            ? authMethod
            : AuthMethod.Unknown;
    }

    private string GetAuthId(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(AuthClaims.Jti)
               ?? principal.FindFirstValue(AuthClaims.ApiKeyId)
               ?? principal.FindFirstValue(AuthClaims.AuthId)
               ?? string.Empty;
    }

    private DateTimeOffset GetExpireAt(ClaimsPrincipal principal)
    {
        var expClaim = principal.FindFirstValue(AuthClaims.Exp) ?? principal.FindFirstValue(AuthClaims.AuthExp);
        if (!string.IsNullOrEmpty(expClaim) && long.TryParse(expClaim, out var expUnix))
        {
            return DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
        }

        return DateTimeOffset.MaxValue;
    }
}
