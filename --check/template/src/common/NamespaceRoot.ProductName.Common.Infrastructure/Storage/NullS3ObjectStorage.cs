using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Storage;

/// <summary>
/// No-op <see cref="IS3ObjectStorage"/> registered when <see cref="IS3Settings.Enabled"/> is false.
/// Accepts writes without persisting; throws on reads (caller should handle gracefully).
/// </summary>
internal sealed class NullS3ObjectStorage(ILogger<NullS3ObjectStorage> logger) : IS3ObjectStorage
{
    public Task PutAsync(string key, byte[] data, string contentType, CancellationToken ct = default)
    {
        logger.LogWarning("NullS3ObjectStorage: PUT {Key} ignored - S3 is disabled", key);
        return Task.CompletedTask;
    }

    public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default)
    {
        logger.LogWarning("NullS3ObjectStorage: GET {Key} - S3 is disabled, returning empty", key);
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<string> GetTextAsync(string key, CancellationToken ct = default)
    {
        logger.LogWarning("NullS3ObjectStorage: GET {Key} - S3 is disabled, returning empty", key);
        return Task.FromResult(string.Empty);
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<Uri> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
        => Task.FromResult(new Uri($"http://storage.disabled.invalid/{key}?s3-disabled=true"));

    public Task CopyAsync(string sourceKey, string targetKey, CancellationToken ct = default)
    {
        logger.LogWarning("NullS3ObjectStorage: COPY {Source} -> {Target} ignored - S3 is disabled", sourceKey, targetKey);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        logger.LogWarning("NullS3ObjectStorage: EXISTS {Key} - S3 is disabled, returning false", key);
        return Task.FromResult(false);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        logger.LogWarning("NullS3ObjectStorage: DELETE {Key} ignored - S3 is disabled", key);
        return Task.CompletedTask;
    }
}
