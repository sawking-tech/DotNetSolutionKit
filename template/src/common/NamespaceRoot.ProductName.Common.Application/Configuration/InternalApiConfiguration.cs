using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Common.Application.Configuration;

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
