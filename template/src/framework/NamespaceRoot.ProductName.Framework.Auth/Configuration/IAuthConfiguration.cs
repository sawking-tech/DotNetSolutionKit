namespace NamespaceRoot.ProductName.Framework.Auth.Configuration;

public interface IAuthConfiguration
{
    bool RequireHttpsMetadata { get; }
    bool SaveToken { get; }
    bool ValidateIssuer { get; }
    bool ValidateAudience { get; }
    bool ValidateLifetime { get; }
    bool ValidateIssuerSigningKey { get; }
}
