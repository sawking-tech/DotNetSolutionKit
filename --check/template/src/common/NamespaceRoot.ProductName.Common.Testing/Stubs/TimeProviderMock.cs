namespace NamespaceRoot.ProductName.Common.Tests.Stubs;

public class TimeProviderMock : TimeProvider
{
    private DateTimeOffset? _fixedUtcNow;

    public TimeProviderMock(DateTimeOffset? fixedUtcNow = null)
    {
        _fixedUtcNow = fixedUtcNow;
    }

    public override DateTimeOffset GetUtcNow() => _fixedUtcNow ?? DateTimeOffset.UtcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _fixedUtcNow = utcNow;

    public void Reset() => _fixedUtcNow = null;
}
