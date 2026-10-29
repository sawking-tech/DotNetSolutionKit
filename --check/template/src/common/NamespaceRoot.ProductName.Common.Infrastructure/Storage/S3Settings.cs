using System.ComponentModel.DataAnnotations;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Storage;

/// <summary>
/// An S3-compatible bucket: AWS S3, or a self-hosted server (SeaweedFS, MinIO) with path-style URLs.
/// </summary>
public interface IS3Settings
{
    /// <summary>When false, a storage that keeps nothing is registered instead of the real client.</summary>
    bool Enabled { get; }

    /// <summary>S3-compatible endpoint (e.g. <c>http://localhost:8333</c> for the local SeaweedFS of the compose files).</summary>
    string ServiceUrl { get; }

    /// <summary>Bucket name (e.g. <c>develop</c>, <c>staging</c>, <c>prod</c>).</summary>
    string BucketName { get; }

    string AccessKey { get; }

    string SecretKey { get; }

    /// <summary>Use path-style URLs. Required for MinIO and SeaweedFS; should be true in all non-AWS environments.</summary>
    bool ForcePathStyle { get; }
}

/// <inheritdoc />
public sealed class S3Settings : IS3Settings
{
    public const string SectionName = "S3";

    public bool Enabled { get; init; } = true;

    [Required(AllowEmptyStrings = false)]
    public string ServiceUrl { get; init; } = "http://localhost:8333";

    [Required(AllowEmptyStrings = false)]
    public string BucketName { get; init; } = "develop";

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public bool ForcePathStyle { get; init; } = true;
}
