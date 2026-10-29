using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

/// <summary>
/// Provides RSA keys for JWT token signing and validation.
/// Inherits public key handling from <see cref="RsaPublicKeyProvider"/>.
/// Used by the Auth service only.
/// </summary>
public class RsaKeyProvider : RsaPublicKeyProvider, IRsaKeyProvider
{
    private readonly ILogger<RsaKeyProvider> _logger;

    public RSA PrivateRsa { get; }
    RSA IRsaKeyProvider.PublicRsa => PublicRsa;
    public RsaSecurityKey SigningKey { get; }

    public RsaKeyProvider(IJwtConfiguration jwtConfig, ILogger<RsaKeyProvider> logger)
        : base(jwtConfig, logger)
    {
        _logger = logger;

        try
        {
            logger.LogInformation("Loading RSA private key from {Path}", jwtConfig.PrivateKeyPath);
            PrivateRsa = LoadKeyFromFile(jwtConfig.PrivateKeyPath, isPrivate: true, logger);
            SigningKey = new RsaSecurityKey(PrivateRsa);

            LogKeyInfo();
            logger.LogInformation("RSA keys loaded successfully");
        }
        catch (Exception ex) when (ex is not ConfigurationException)
        {
            logger.LogError(ex, "Failed to load RSA private key");
            throw new ConfigurationException($"Failed to initialize RSA signing key: {ex.Message}", ex);
        }
    }

    public SigningCredentials GetSigningCredentials() =>
        new(SigningKey, SecurityAlgorithms.RsaSha256);

    private void LogKeyInfo()
    {
        _logger.LogDebug("RSA keys loaded - Private: {PrivateKeySize} bits, Public: {PublicKeySize} bits",
            PrivateRsa.KeySize, PublicRsa.KeySize);

        if (PrivateRsa.KeySize < 2048 || PublicRsa.KeySize < 2048)
        {
            _logger.LogWarning(
                "RSA key size less than 2048 bits (Private: {PrivateKeySize}, Public: {PublicKeySize}) - consider upgrading",
                PrivateRsa.KeySize, PublicRsa.KeySize);
        }
    }
}
