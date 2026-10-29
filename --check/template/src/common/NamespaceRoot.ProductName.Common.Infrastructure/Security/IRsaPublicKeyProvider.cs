using Microsoft.IdentityModel.Tokens;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

/// <summary>
/// Provides the RSA public key for JWT token validation.
/// Used by services that validate but do not sign tokens (Gateway, Core, Billing, etc.).
/// Services that also sign tokens use <see cref="IRsaKeyProvider"/>.
/// </summary>
public interface IRsaPublicKeyProvider
{
    /// <summary>Security key for JWT validation (public key).</summary>
    RsaSecurityKey ValidationKey { get; }
}
