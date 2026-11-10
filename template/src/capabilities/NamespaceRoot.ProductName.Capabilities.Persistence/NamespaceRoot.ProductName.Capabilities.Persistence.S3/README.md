# Capabilities.Persistence.S3

S3-compatible object storage of the flag `--Storage`: `IS3ObjectStorage` behind which the service puts,
reads, lists, copies and deletes objects in one bucket, registered from the `S3` section by
`AddS3ObjectStorage` in a service that uses it; with `S3:Enabled=false` a storage that keeps nothing, so
a service runs before it has a bucket.

A service references this project only with the flag, so only that service carries the S3 SDK. Callers
stay free of the SDK: a missing object is a `NotFoundException` (404), any other failure a
`StorageException` (503 for the client).
