using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration;

/// <summary>
/// Settings interface for RabbitMQ transport configuration.
/// </summary>
public interface IRabbitMqSettings
{
    /// <summary>
    /// RabbitMQ server hostname or IP address.
    /// </summary>
    string Host { get; }

    /// <summary>
    /// Username for RabbitMQ authentication.
    /// </summary>
    string UserName { get; }

    /// <summary>
    /// Password for RabbitMQ authentication.
    /// </summary>
    string Password { get; }

    /// <summary>
    /// When true, all queues are purged on application startup.
    /// Useful for development; must be false in production.
    /// </summary>
    bool PurgeOnStartup { get; }

    /// <summary>
    /// Maximum number of retry attempts before a message is moved to the error queue.
    /// </summary>
    int RetryLimit { get; }

    /// <summary>
    /// Delay in seconds before the first retry attempt.
    /// </summary>
    int RetryInitialIntervalSeconds { get; }

    /// <summary>
    /// Additional delay in seconds added to each subsequent retry attempt.
    /// </summary>
    int RetryIntervalIncrementSeconds { get; }
}

/// <summary>
/// Settings implementation for RabbitMQ transport.
/// </summary>
public class RabbitMqSettings : IRabbitMqSettings
{
    public const string SectionName = "RabbitMq";

    public bool Enabled { get; set; }

    [Required]
    public string Host { get; set; } = "localhost";

    [Required]
    public string UserName { get; set; } = "guest";

    [Required]
    public string Password { get; set; } = "guest";

    public bool PurgeOnStartup { get; set; }

    [Range(1, 10)]
    public int RetryLimit { get; set; } = 3;

    [Range(1, 60)]
    public int RetryInitialIntervalSeconds { get; set; } = 1;

    [Range(1, 60)]
    public int RetryIntervalIncrementSeconds { get; set; } = 2;
}
