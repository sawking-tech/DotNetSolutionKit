namespace NamespaceRoot.ProductName.Common.Application.Configuration;

/// <summary>
/// JWT configuration for services that only validate tokens (public key only).
/// </summary>
public interface IJwtPublicConfiguration
{
    string Issuer { get; }
    string Audience { get; }
    int LifetimeMinutes { get; }
    string PublicKeyPath { get; }
}

/// <summary>
/// Full JWT configuration for services that sign and validate tokens (Auth service).
/// </summary>
public interface IJwtConfiguration : IJwtPublicConfiguration
{
    string PrivateKeyPath { get; }
}

public interface IRefreshTokenConfiguration
{
    int ExpirationDays { get; }
    int Size { get; }

    /// <summary>
    /// Cookie Path attribute — restricts which paths the browser sends the refresh token cookie to.
    /// Set to the gateway-facing prefix of cookie-aware auth endpoints (e.g. "/api/auth/v2/auth").
    /// Must line up with the actual gateway route prefix in every environment; a mismatched path
    /// silently drops the cookie on refresh calls without any error surface. Defaults to "/" if
    /// left blank.
    /// </summary>
    string CookiePath { get; }

    /// <summary>
    /// Window (in seconds) after a token was rotated during which a repeat refresh with the old
    /// cookie is treated as a concurrent client race and answered with a silent 401 — no
    /// security-alert email, no revoke-all cascade. Outside this window a repeat with a revoked
    /// cookie is treated as replay and triggers the alert. Default 30 seconds.
    /// </summary>
    int ReuseGraceWindowSeconds { get; }

    /// <summary>
    /// Minimum interval (in seconds) between two security-alert emails to the same user.
    /// Prevents a stuck client with a stale cookie from producing an email storm. Default 3600
    /// seconds (1 hour).
    /// </summary>
    int SecurityAlertThrottleSeconds { get; }
}

public interface IInternalApiConfiguration
{
    string ApiKey { get; }
}

public interface IAuthConfiguration
{
    bool RequireHttpsMetadata { get; }
    bool SaveToken { get; }
    bool ValidateIssuer { get; }
    bool ValidateAudience { get; }
    bool ValidateLifetime { get; }
    bool ValidateIssuerSigningKey { get; }
}
