using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

/// <summary>
/// Provides RSA keys for JWT token signing and validation.
/// For validation-only services use <see cref="IRsaPublicKeyProvider"/>.
/// </summary>
public interface IRsaKeyProvider : IRsaPublicKeyProvider
{
    /// <summary>RSA instance with private key for signing tokens.</summary>
    RSA PrivateRsa { get; }

    /// <summary>RSA instance with public key for token validation.</summary>
    RSA PublicRsa { get; }

    /// <summary>Security key for signing (private key).</summary>
    RsaSecurityKey SigningKey { get; }

    /// <summary>Gets the signing credentials for JWT token generation.</summary>
    SigningCredentials GetSigningCredentials();
}
