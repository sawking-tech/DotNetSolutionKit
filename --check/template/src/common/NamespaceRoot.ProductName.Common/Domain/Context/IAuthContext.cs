namespace NamespaceRoot.ProductName.Common.Domain.Context;

/// <summary>
/// Current authentication context data
/// </summary>
public interface IAuthContext
{
    /// <summary>
    /// Authentication type (JWT, API_KEY, etc.)
    /// </summary>
    AuthMethod Type { get; }

    /// <summary>
    /// Authentication identifier (JWT ID or API Key ID)
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Authentication expiration date and time
    /// </summary>
    DateTimeOffset ExpireAt { get; }
}
