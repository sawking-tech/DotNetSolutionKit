using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Framework.Auth.Configuration;

/// <summary>
/// JWT configuration for services that only validate tokens.
/// </summary>
public class JwtPublicConfiguration : IJwtPublicConfiguration
{
    public const string SectionName = "Jwt";

    [Required(ErrorMessage = "JWT issuer is required")]
    public string Issuer { get; init; } = string.Empty;

    [Required(ErrorMessage = "JWT audience is required")]
    public string Audience { get; init; } = string.Empty;

    [Range(1, 1440, ErrorMessage = "JWT lifetime must be between 1 and 1440 minutes (24 hours)")]
    public int LifetimeMinutes { get; init; } = 60;

    [Required(ErrorMessage = "Public key path is required")]
    public string PublicKeyPath { get; init; } = string.Empty;
}

/// <summary>
/// Full JWT configuration for the service that signs and validates tokens.
/// </summary>
public class JwtConfiguration : JwtPublicConfiguration, IJwtConfiguration
{
    [Required(ErrorMessage = "Private key path is required")]
    public string PrivateKeyPath { get; init; } = string.Empty;
}

public class RefreshTokenConfiguration : IRefreshTokenConfiguration
{
    public const string SectionName = "RefreshTokenOptions";

    [Range(1, 365, ErrorMessage = "Refresh token expiration must be between 1 and 365 days")]
    public int ExpirationDays { get; init; } = 30;

    [Range(16, 64, ErrorMessage = "Refresh token size must be between 16 and 64 bytes")]
    public int Size { get; init; } = 32;

    public string CookiePath { get; init; } = "/api/auth/v2/auth";

    [Range(0, 300, ErrorMessage = "Reuse grace window must be between 0 and 300 seconds")]
    public int ReuseGraceWindowSeconds { get; init; } = 30;

    [Range(0, 86400, ErrorMessage = "Security alert throttle must be between 0 and 86400 seconds (24h)")]
    public int SecurityAlertThrottleSeconds { get; init; } = 3600;
}
