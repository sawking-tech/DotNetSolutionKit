namespace NamespaceRoot.ProductName.Common.Infrastructure.Storage;

/// <summary>
/// S3-compatible object storage: AWS S3, SeaweedFS, MinIO. Callers stay free of the SDK; a missing object is a
/// <c>NotFoundException</c>, any other failure a <c>StorageException</c>.
/// </summary>
public interface IS3ObjectStorage
{
    /// <summary>Uploads <paramref name="data"/> at <paramref name="key"/>.</summary>
    Task PutAsync(string key, byte[] data, string contentType, CancellationToken ct = default);

    /// <summary>Downloads the object at <paramref name="key"/> as raw bytes.</summary>
    Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default);

    /// <summary>Downloads the object at <paramref name="key"/> as a UTF-8 string.</summary>
    Task<string> GetTextAsync(string key, CancellationToken ct = default);

    /// <summary>Lists all object keys under <paramref name="prefix"/>.</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default);

    /// <summary>Returns a pre-signed GET URL valid for <paramref name="ttl"/>.</summary>
    Task<Uri> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Copies the object at <paramref name="sourceKey"/> to <paramref name="targetKey"/>
    /// within the same bucket. The source is not deleted.</summary>
    Task CopyAsync(string sourceKey, string targetKey, CancellationToken ct = default);

    /// <summary>Checks whether an object exists at <paramref name="key"/>.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>Removes the object at <paramref name="key"/>. Idempotent - a missing key is a no-op.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
