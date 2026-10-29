using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;

namespace NamespaceRoot.ProductName.Capabilities.Mongo;

/// <summary>
/// Connection settings for MongoDB, the <c>MongoDB</c> section.
/// </summary>
public sealed class MongoOptions : IValidatableObject
{
    public const string SectionName = "MongoDB";

    /// <summary>
    /// Whether MongoDB is used. When <c>false</c>, asking for the database answers 503 and neither
    /// <see cref="ConnectionString"/> nor <see cref="Database"/> is required.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>A MongoDB connection string: <c>mongodb://user:password@host:27017</c>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>The database of this service; every collection it uses lives there.</summary>
    public string Database { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled)
            yield break;

        if (string.IsNullOrWhiteSpace(ConnectionString))
            yield return new ValidationResult(
                "MongoDB:ConnectionString is required when MongoDB:Enabled is true.", [nameof(ConnectionString)]);
        else if (!IsConnectionString(ConnectionString))
            yield return new ValidationResult(
                "MongoDB:ConnectionString is not a MongoDB connection string: mongodb://user:password@host:27017.",
                [nameof(ConnectionString)]);

        if (string.IsNullOrWhiteSpace(Database))
            yield return new ValidationResult(
                "MongoDB:Database is required when MongoDB:Enabled is true.", [nameof(Database)]);
    }

    private static bool IsConnectionString(string value)
    {
        try
        {
            MongoUrl.Create(value);
            return true;
        }
        catch (MongoConfigurationException)
        {
            return false;
        }
    }
}
