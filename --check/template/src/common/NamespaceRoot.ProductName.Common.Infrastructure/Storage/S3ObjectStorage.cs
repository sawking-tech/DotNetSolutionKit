using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using NamespaceRoot.ProductName.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Storage;

/// <summary>
/// S3 SDK implementation of <see cref="IS3ObjectStorage"/>.
/// AmazonS3Exception is translated to domain exceptions here; callers stay SDK-free.
/// </summary>
internal sealed class S3ObjectStorage : IS3ObjectStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<S3ObjectStorage> _logger;

    public S3ObjectStorage(IS3Settings settings, ILogger<S3ObjectStorage> logger)
    {
        _bucket = settings.BucketName;
        _logger = logger;

        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
            new AmazonS3Config
            {
                ServiceURL     = settings.ServiceUrl,
                ForcePathStyle = settings.ForcePathStyle,
                // A broken endpoint must fail fast, so a caller with a fallback uses it at once; the
                // SDK defaults (100 s timeout × 4 retries) pin the request thread for minutes and turn
                // a missing storage server into a request that seems to hang.
                Timeout        = TimeSpan.FromSeconds(2),
                MaxErrorRetry  = 1,
            });
    }

    /// <inheritdoc />
    public async Task PutAsync(string key, byte[] data, string contentType, CancellationToken ct = default)
    {
        try
        {
            using var stream = new MemoryStream(data);
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName  = _bucket,
                Key         = key,
                InputStream = stream,
                ContentType = contentType,
            }, ct);
            _logger.LogDebug("S3: PUT {Bucket}/{Key}", _bucket, key);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3: PUT {Bucket}/{Key} failed", _bucket, key);
            throw new StorageException($"Failed to store object '{key}'.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default)
    {
        try
        {
            using var response = await _s3.GetObjectAsync(_bucket, key, ct);
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException($"S3 object '{key}' not found in bucket '{_bucket}'.");
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3: GET bytes {Bucket}/{Key} failed", _bucket, key);
            throw new StorageException($"Failed to read object '{key}'.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetTextAsync(string key, CancellationToken ct = default)
    {
        try
        {
            using var response = await _s3.GetObjectAsync(_bucket, key, ct);
            using var reader   = new StreamReader(response.ResponseStream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException($"S3 object '{key}' not found in bucket '{_bucket}'.");
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3: GET text {Bucket}/{Key} failed", _bucket, key);
            throw new StorageException($"Failed to read object '{key}'.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            var keys    = new List<string>();
            var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix };

            ListObjectsV2Response response;
            do
            {
                response = await _s3.ListObjectsV2Async(request, ct);
                // SDK 4 leaves an empty list null instead of empty.
                keys.AddRange(response.S3Objects?.Select(o => o.Key) ?? []);
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);

            return keys;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3: LIST {Bucket}/{Prefix} failed", _bucket, prefix);
            throw new StorageException($"Failed to list objects under '{prefix}'.", ex);
        }
    }

    /// <inheritdoc />
    public Task<Uri> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key        = key,
            Expires    = DateTime.UtcNow.Add(ttl),
            Verb       = HttpVerb.GET,
        });
        return Task.FromResult(new Uri(url));
    }

    /// <inheritdoc />
    public async Task CopyAsync(string sourceKey, string targetKey, CancellationToken ct = default)
    {
        try
        {
            await _s3.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket      = _bucket,
                SourceKey         = sourceKey,
                DestinationBucket = _bucket,
                DestinationKey    = targetKey,
            }, ct);
            _logger.LogDebug("S3: COPY {Bucket}/{Source} -> {Bucket}/{Target}", _bucket, sourceKey, _bucket, targetKey);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException($"S3 object '{sourceKey}' not found in bucket '{_bucket}'.");
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3: COPY {Bucket}/{Source} -> {Bucket}/{Target} failed", _bucket, sourceKey, _bucket, targetKey);
            throw new StorageException($"Failed to copy object '{sourceKey}' to '{targetKey}'.", ex);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _s3.DeleteObjectAsync(_bucket, key, ct);
            _logger.LogDebug("S3: DELETE {Bucket}/{Key}", _bucket, key);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Idempotent: the object was already gone. Nothing to do.
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3: DELETE {Bucket}/{Key} failed", _bucket, key);
            throw new StorageException($"Failed to delete object '{key}'.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3: HEAD {Bucket}/{Key} failed", _bucket, key);
            throw new StorageException($"Failed to probe object '{key}'.", ex);
        }
    }
}
