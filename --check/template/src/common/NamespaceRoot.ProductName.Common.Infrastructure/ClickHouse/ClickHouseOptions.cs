using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;

/// <summary>
/// Connection settings for ClickHouse, the <c>ClickHouse</c> section.
/// </summary>
public sealed class ClickHouseOptions : IValidatableObject
{
    public const string SectionName = "ClickHouse";

    /// <summary>
    /// Whether ClickHouse is used. When <c>false</c>, opening a connection answers 503 and
    /// <see cref="ConnectionString"/> is not required.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>ADO.NET-style connection string: host, port, credentials, database.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Per-command timeout in seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Largest insert handed to ClickHouse as a single block.
    /// </summary>
    /// <remarks>
    /// Doubles as the cutoff for server-side insert deduplication: the token is matched per block,
    /// so a write larger than this spans several blocks and cannot carry one. Writes at or below it
    /// get the token and are idempotent at the server; larger writes rely on a
    /// <c>ReplacingMergeTree</c> collapsing them at merge.
    /// </remarks>
    public int MaxInsertBlockRows { get; set; } = 10_000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Enabled && string.IsNullOrWhiteSpace(ConnectionString))
            yield return new ValidationResult(
                "ClickHouse:ConnectionString is required when ClickHouse:Enabled is true.",
                [nameof(ConnectionString)]);
    }
}
