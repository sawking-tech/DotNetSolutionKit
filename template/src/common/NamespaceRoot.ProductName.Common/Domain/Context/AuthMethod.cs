namespace NamespaceRoot.ProductName.Common.Domain.Context;

/// <summary>
/// Authentication methods
/// </summary>
public enum AuthMethod
{
    Unknown = 0,

    /// <summary>
    /// JWT token authentication
    /// </summary>
    Jwt = 1,

    /// <summary>
    /// API Key authentication
    /// </summary>
    ApiKey = 2,

    /// <summary>
    /// System/internal authentication
    /// </summary>
    System = 3,

    /// <summary>
    /// By password reset token
    /// </summary>
    PasswordReset = 4
}
