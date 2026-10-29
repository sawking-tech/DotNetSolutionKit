namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// An object storage operation failed (S3, MinIO). The client gets 503: the request may succeed later,
/// and nothing in it was wrong.
/// </summary>
public class StorageException(string? message, Exception? innerException = null)
    : ServiceUnavailableException(message, innerException!, "STORAGE_UNAVAILABLE");
