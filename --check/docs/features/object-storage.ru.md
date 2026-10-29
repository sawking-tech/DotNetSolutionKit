# Объектное хранилище

`--Storage` добавляет в `Common` S3-совместимое объектное хранилище и регистрирует его в сервисе: для
файлов, которые сервис держит вне своей базы, например документов, изображений, сгенерированных PDF.

```csharp
public class InvoiceFiles(IS3ObjectStorage storage)
{
    public Task SaveAsync(Guid invoiceId, byte[] pdf, CancellationToken ct) =>
        storage.PutAsync($"invoices/{invoiceId}.pdf", pdf, "application/pdf", ct);

    public Task<Uri> LinkAsync(Guid invoiceId, CancellationToken ct) =>
        storage.GetPresignedUrlAsync($"invoices/{invoiceId}.pdf", TimeSpan.FromMinutes(15), ct);
}
```

`IS3ObjectStorage` кладёт, читает, перечисляет, копирует, проверяет и удаляет объекты в одном бакете и
выдаёт pre-signed ссылки. Сервис не зависит от SDK: отсутствующий объект - это `NotFoundException` (404),
любой другой сбой - `StorageException`, который клиент получает как 503.

## Настройки

| Настройка | |
|---|---|
| `S3:Enabled` | `true`; `false` ничего не хранит: записи игнорируются, чтения возвращают пустое, поэтому сервис работает ещё до появления бакета |
| `S3:ServiceUrl` | эндпоинт: AWS или собственный сервер |
| `S3:BucketName` | бакет, который должен существовать |
| `S3:AccessKey`, `S3:SecretKey` | учётные данные, из окружения или хранилища секретов, никогда не в файле |
| `S3:ForcePathStyle` | `true` для собственных серверов |

Запрос падает через 2 секунды и повторяется один раз: вызывающий с запасным вариантом переходит на него
сразу, а не ждёт минутных таймаутов SDK по умолчанию.

## Под docker compose

С `--Deploy compose` инфраструктура получает [SeaweedFS](https://github.com/seaweedfs/seaweedfs)
(Apache 2.0) как S3-сервер и одноразовый контейнер, который создаёт бакет до старта сервисов. MinIO,
обычный выбор, больше не публикует образы. Учётные данные и бакет берутся из `deploy/compose/.env`:
`S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_BUCKET`.

## Тесты

`Common.Tests/Integration/S3ObjectStorageTests` работает с реальным сервером, когда задан `TEST_S3`,
например с контейнером SeaweedFS:

```bash
eval "$(bash tests/servers/up.sh)"     # тестовые серверы, среди них хранилище и его бакет (tests/servers/s3.sh)
dotnet test --filter "FullyQualifiedName~S3ObjectStorage"
```

Тест сохраняет, читает, перечисляет, копирует и удаляет, перечисляет пустой префикс и сообщает об
отсутствующем объекте.
