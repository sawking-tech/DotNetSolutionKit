using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration;

/// <summary>
/// Settings interface for Hangfire background jobs and dashboard authorization
/// </summary>
public interface IHangfireSettings
{
    /// <summary>
    /// Username for Dashboard Basic Authentication
    /// </summary>
    string DashboardUser { get; }

    /// <summary>
    /// Password for Dashboard Basic Authentication
    /// </summary>
    string DashboardPassword { get; }

    /// <summary>
    /// Number of parallel workers for job processing
    /// </summary>
    int WorkerCount { get; }
}

/// <summary>
/// Settings implementation for Hangfire background jobs
/// </summary>
public class HangfireSettings : IHangfireSettings
{
    public const string SectionName = "HangfireSettings";

    /// <inheritdoc />
    [Required(ErrorMessage = "Hangfire Dashboard username is required")]
    public string DashboardUser { get; set; } = "admin";

    /// <inheritdoc />
    [Required(ErrorMessage = "Hangfire Dashboard password is required")]
    [MinLength(12, ErrorMessage = "Dashboard password must be at least 12 characters")]
    public string DashboardPassword { get; set; } = string.Empty;

    /// <inheritdoc />
    [Range(1, 100, ErrorMessage = "Worker count must be between 1 and 100")]
    public int WorkerCount { get; set; } = 10;
}
