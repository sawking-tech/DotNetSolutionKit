namespace NamespaceRoot.ProductName.Framework.Auth.Configuration;

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
