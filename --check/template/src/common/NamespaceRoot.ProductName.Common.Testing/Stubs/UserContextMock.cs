using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Tests.Stubs;

public class UserContextMock : IUserContext
{
    public UserContextMock(
        string userId = TestDomainExecutionContext.DefaultTestUserId,
        string login = "testuser@example.com",
        string displayName = "Test User",
        Guid? apiKeyId = null,
        IAuthContext? authContext = null)
    {
        UserId = Guid.Parse(userId);
        Login = login;
        DisplayName = displayName;
        ApiKeyId = apiKeyId;
        AuthContext = authContext ?? AuthContextMock.CreateJwtAuth(TimeProvider.System);
    }

    public Guid UserId { get; set; }
    public Guid? TenantId { get; set; }
    public string? Login { get; set; }
    public string? DisplayName { get; set; }
    public Guid? ApiKeyId { get; set; }
    public IAuthContext AuthContext { get; set; }
}

public class AuthContextMock : IAuthContext
{
    public AuthContextMock(AuthMethod type, string id, DateTimeOffset expireAt)
    {
        Type = type;
        Id = id;
        ExpireAt = expireAt;
    }

    public AuthMethod Type { get; }
    public string Id { get; }
    public DateTimeOffset ExpireAt { get; }

    public static AuthContextMock CreateJwtAuth(TimeProvider timeProvider, string? jwtId = null, DateTimeOffset? expireAt = null)
        => new(AuthMethod.Jwt, jwtId ?? $"jwt-{Guid.NewGuid()}", expireAt ?? timeProvider.GetUtcNow().AddDays(30));

    public static AuthContextMock CreateApiKeyAuth(TimeProvider timeProvider, Guid apiKeyId, DateTimeOffset? expireAt = null)
        => new(AuthMethod.ApiKey, apiKeyId.ToString(), expireAt ?? timeProvider.GetUtcNow().AddDays(30));

    public static AuthContextMock CreateSystemAuth()
        => new(AuthMethod.System, "00000000-0000-0000-0000-000000000000", DateTimeOffset.MaxValue);

    public static AuthContextMock CreateExpiredAuth(TimeProvider timeProvider, AuthMethod type = AuthMethod.Jwt)
        => new(type, type == AuthMethod.ApiKey ? Guid.NewGuid().ToString() : $"expired-{Guid.NewGuid()}", timeProvider.GetUtcNow().AddDays(-1));
}

public static class UserContextMockFactory
{
    public static UserContextMock CreateJwtUser(
        TimeProvider timeProvider,
        string userId = TestDomainExecutionContext.DefaultTestUserId,
        string login = "testuser@example.com",
        string displayName = "Test User",
        string? jwtId = null,
        DateTimeOffset? jwtExpireAt = null)
        => new(userId: userId, login: login, displayName: displayName,
            authContext: AuthContextMock.CreateJwtAuth(timeProvider, jwtId, jwtExpireAt));

    public static UserContextMock CreateApiKeyUser(
        TimeProvider timeProvider,
        Guid apiKeyId,
        string userId = TestDomainExecutionContext.DefaultApiKeyUserId,
        DateTimeOffset? apiKeyExpireAt = null)
        => new(userId: userId, login: "apikey@system", displayName: "API Key User", apiKeyId: apiKeyId,
            authContext: AuthContextMock.CreateApiKeyAuth(timeProvider, apiKeyId, apiKeyExpireAt));

    public static UserContextMock CreateSystemUser()
        => new(userId: TestDomainExecutionContext.DefaultTestUserId, login: "system", displayName: "System",
            authContext: AuthContextMock.CreateSystemAuth());

    public static UserContextMock CreateExpiredJwtUser(TimeProvider timeProvider)
        => new(userId: "00000000-0000-0000-0000-000000000003", login: "expired@example.com", displayName: "Expired User",
            authContext: AuthContextMock.CreateExpiredAuth(timeProvider, AuthMethod.Jwt));

    public static UserContextMock CreateExpiredApiKeyUser(TimeProvider timeProvider, Guid apiKeyId)
        => new(userId: "00000000-0000-0000-0000-000000000004", login: "expired-api@system", displayName: "Expired API User",
            apiKeyId: apiKeyId, authContext: AuthContextMock.CreateExpiredAuth(timeProvider, AuthMethod.ApiKey));
}
