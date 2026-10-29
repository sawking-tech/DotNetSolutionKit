using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Tests.Stubs;

namespace NamespaceRoot.ProductName.Common.Tests;

public class TestDomainExecutionContext : IDomainExecutionContext
{
    public const string DefaultTestUserId = "00000000-0000-0000-0000-000000000001";
    public const string DefaultApiKeyUserId = "00000000-0000-0000-0000-000000000002";

    public TestDomainExecutionContext(IUserContext actor, TimeProvider timeProvider)
    {
        Actor = actor;
        TimeProvider = timeProvider;
    }

    public IUserContext Actor { get; private set; }

    public TimeProvider TimeProvider { get; }

    public void SetActor(IUserContext userContext) => Actor = userContext;

    public void SetActor(string userId, string login, string displayName = "Test User")
        => Actor = new UserContextMock(userId, login, displayName);

    public void SetJwtActor(
        string userId = DefaultTestUserId,
        string login = "testuser@example.com",
        string displayName = "Test User")
        => Actor = UserContextMockFactory.CreateJwtUser(TimeProvider, userId, login, displayName);

    public void SetApiKeyActor(Guid apiKeyId, string userId = DefaultApiKeyUserId)
        => Actor = UserContextMockFactory.CreateApiKeyUser(TimeProvider, apiKeyId, userId);

    public void SetSystemActor() => Actor = UserContextMockFactory.CreateSystemUser();
}
