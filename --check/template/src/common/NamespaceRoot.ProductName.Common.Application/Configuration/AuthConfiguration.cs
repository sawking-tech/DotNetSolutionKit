using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Common.Application.Configuration;

/// <summary>
/// JWT configuration for services that only validate tokens (API Gateway, Core, Billing, etc.).
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
/// Full JWT configuration for the Auth service (signs and validates tokens).
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

    /// <summary>
    /// Default matches the gateway-facing prefix of the v2 auth endpoints. If the concrete
    /// project uses a different gateway prefix, override this per-environment. Leaving it
    /// blank falls back to "/" which is safe but leaks the refresh cookie on every request.
    /// </summary>
    public string CookiePath { get; init; } = "/api/auth/v2/auth";

    [Range(0, 300, ErrorMessage = "Reuse grace window must be between 0 and 300 seconds")]
    public int ReuseGraceWindowSeconds { get; init; } = 30;

    [Range(0, 86400, ErrorMessage = "Security alert throttle must be between 0 and 86400 seconds (24h)")]
    public int SecurityAlertThrottleSeconds { get; init; } = 3600;
}

/// <remarks>
/// The key is optional: without one, every call that presents an API key is refused, so a service
/// generated and deployed before services call each other starts with its internal API closed rather
/// than failing to start.
/// </remarks>
public class InternalApiConfiguration : IInternalApiConfiguration, IValidatableObject
{
    public const string SectionName = "InternalApi";

    private const int MinimumKeyLength = 16;

    public string ApiKey { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ApiKey.Length is > 0 and < MinimumKeyLength)
            yield return new ValidationResult(
                $"Internal API key must be at least {MinimumKeyLength} characters", [nameof(ApiKey)]);
    }
}

public class AuthConfiguration : IAuthConfiguration
{
    public const string SectionName = "Auth";

    public bool RequireHttpsMetadata { get; init; } = true;
    public bool SaveToken { get; init; } = true;
    public bool ValidateIssuer { get; init; } = true;
    public bool ValidateAudience { get; init; } = true;
    public bool ValidateLifetime { get; init; } = true;
    public bool ValidateIssuerSigningKey { get; init; } = true;
}
