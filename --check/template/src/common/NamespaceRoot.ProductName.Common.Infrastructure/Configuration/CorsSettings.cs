using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration;

/// <summary>
/// Interface for CORS configuration settings
/// </summary>
public interface ICorsSettings
{
    /// <summary>
    /// Array of allowed origins for CORS
    /// </summary>
    string[] AllowedOrigins { get; }

    /// <summary>
    /// Array of allowed HTTP methods
    /// </summary>
    string[] AllowedMethods { get; }

    /// <summary>
    /// Whether to allow credentials (cookies, auth headers)
    /// </summary>
    bool AllowCredentials { get; }

    /// <summary>
    /// Preflight cache duration in seconds
    /// </summary>
    int PreflightMaxAge { get; }

    /// <summary>
    /// Array of allowed headers (if empty, allows any)
    /// </summary>
    string[] AllowedHeaders { get; }

    /// <summary>
    /// Array of exposed headers
    /// </summary>
    string[] ExposedHeaders { get; }
}

/// <summary>
/// Settings implementation for CORS configuration
/// </summary>
public class CorsSettings : ICorsSettings
{
    public const string SectionName = "Cors";

    /// <inheritdoc />
    public string[] AllowedOrigins { get; set; } = [];

    /// <inheritdoc />
    public string[] AllowedMethods { get; set; } = ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"];

    /// <inheritdoc />
    public bool AllowCredentials { get; set; } = true;

    /// <inheritdoc />
    [Range(0, 86400, ErrorMessage = "Preflight max age must be between 0 and 86400 seconds")]
    public int PreflightMaxAge { get; set; } = 3600;

    /// <inheritdoc />
    public string[] AllowedHeaders { get; set; }  = [];

    /// <inheritdoc />
    public string[] ExposedHeaders { get; set; } = [];
}
