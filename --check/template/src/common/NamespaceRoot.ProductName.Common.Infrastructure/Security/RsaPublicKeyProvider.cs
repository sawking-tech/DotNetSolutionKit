using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

/// <summary>
/// Loads the RSA public key for JWT token validation.
/// Used by services that validate but do not sign tokens (Gateway, Core, Billing, etc.).
/// Services that also sign tokens use <see cref="RsaKeyProvider"/>.
/// </summary>
public class RsaPublicKeyProvider : IRsaPublicKeyProvider
{
    protected readonly RSA PublicRsa;
    public RsaSecurityKey ValidationKey { get; }

    public RsaPublicKeyProvider(IJwtPublicConfiguration jwtConfig, ILogger<RsaPublicKeyProvider> logger)
        : this(jwtConfig, (ILogger)logger) { }

    protected RsaPublicKeyProvider(IJwtPublicConfiguration jwtConfig, ILogger logger)
    {
        logger.LogInformation("Loading RSA public key from {Path}", jwtConfig.PublicKeyPath);
        PublicRsa = LoadKeyFromFile(jwtConfig.PublicKeyPath, isPrivate: false, logger);
        ValidationKey = new RsaSecurityKey(PublicRsa);
        logger.LogInformation("RSA public key loaded ({KeySize} bits)", PublicRsa.KeySize);
    }

    protected static RSA LoadKeyFromFile(string filePath, bool isPrivate, ILogger logger)
    {
        var keyType = isPrivate ? "Private" : "Public";
        try
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"{keyType} key file not found", filePath);

            var keyContent = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(keyContent))
                throw new ConfigurationException($"{keyType} key file is empty: {filePath}");

            var rsa = RSA.Create();
            rsa.ImportFromPem(keyContent);

            logger.LogDebug("Successfully loaded {KeyType} key from {FilePath}", keyType, filePath);
            return rsa;
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (FileNotFoundException ex)
        {
            logger.LogError(ex, "{KeyType} key file not found", keyType);
            throw new ConfigurationException($"{keyType} key file not found: {filePath}", ex);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "Invalid {KeyType} key format", keyType);
            throw new ConfigurationException($"Invalid {keyType} key format in file: {filePath}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied to {KeyType} key file", keyType);
            throw new ConfigurationException($"Access denied to {keyType} key file: {filePath}", ex);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "IO error reading {KeyType} key file", keyType);
            throw new ConfigurationException($"IO error reading {keyType} key file: {filePath}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error loading {KeyType} key", keyType);
            throw new ConfigurationException($"Unexpected error loading {keyType} key: {ex.Message}", ex);
        }
    }
}
