# Object storage

`--Storage` adds S3-compatible object storage to `Common` and registers it in the service: files a
service keeps outside its database, such as documents, images, generated PDFs.

```csharp
public class InvoiceFiles(IS3ObjectStorage storage)
{
    public Task SaveAsync(Guid invoiceId, byte[] pdf, CancellationToken ct) =>
        storage.PutAsync($"invoices/{invoiceId}.pdf", pdf, "application/pdf", ct);

    public Task<Uri> LinkAsync(Guid invoiceId, CancellationToken ct) =>
        storage.GetPresignedUrlAsync($"invoices/{invoiceId}.pdf", TimeSpan.FromMinutes(15), ct);
}
```

`IS3ObjectStorage` puts, reads, lists, copies, checks and deletes objects in one bucket, and gives
pre-signed links. A service stays free of the SDK: a missing object is a `NotFoundException` (404), any
other failure a `StorageException`, which the client receives as 503.

## Settings

| Setting | |
|---|---|
| `S3:Enabled` | `true`; `false` keeps nothing: writes are ignored, reads return empty, so a service runs before it has a bucket |
| `S3:ServiceUrl` | the endpoint: AWS, or a self-hosted server |
| `S3:BucketName` | the bucket, which has to exist |
| `S3:AccessKey`, `S3:SecretKey` | credentials, from the environment or the secret store, never in a file |
| `S3:ForcePathStyle` | `true` for self-hosted servers |

A request fails after 2 seconds and is retried once: a caller with a fallback uses it at once instead of
waiting for the SDK's defaults of minutes.

## Under docker compose

With `--Deploy compose`, the infrastructure gets [SeaweedFS](https://github.com/seaweedfs/seaweedfs)
(Apache 2.0) as the S3 server, and a one-off container that creates the bucket before the services
start. MinIO, the usual choice, no longer publishes images. The credentials and the bucket come from
`deploy/compose/.env`: `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_BUCKET`.

## Tests

`Common.Tests/Integration/S3ObjectStorageTests` runs against a real server when `TEST_S3` is set, for
example to a SeaweedFS container:

```bash
eval "$(bash tests/servers/up.sh)"     # the test servers, the storage and its bucket among them (tests/servers/s3.sh)
dotnet test --filter "FullyQualifiedName~S3ObjectStorage"
```

It stores, reads, lists, copies and deletes, lists an empty prefix and reports a missing object.
