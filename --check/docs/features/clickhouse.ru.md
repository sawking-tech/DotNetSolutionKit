# ClickHouse

`--ClickHouse` добавляет в `Common` то, что нужно каждому сервису, который читает или пишет ClickHouse, и
регистрирует это в сервисе. Запросы остаются в сервисе: читатель или писатель - класс инфраструктуры
сервиса за портом его слоя приложения.

| Часть | |
|---|---|
| `IClickHouseConnections` | подключения поверх одного общего HTTP-клиента, с распаковкой ответов, которая нужна ClickHouse; 503, когда выключено |
| `ClickHouseSchemaGuard` | при старте отказывается запускаться, если в таблице нет колонки, которую называет insert, и говорит, что применить |
| `ClickHouseValues` | единое приведение слабо типизированных значений драйвера и параметры пагинации для постраничного запроса |
| готовность | `/ready` проверяет `SELECT 1`, пока ClickHouse включён |

```csharp
public sealed class UsageReader(IClickHouseConnections connections) : IUsageReader
{
    public async Task<IReadOnlyList<Usage>> PageAsync(int page, int size, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, amount FROM reports.usage ORDER BY id LIMIT {p_page_size:UInt32} OFFSET {p_offset:UInt64}";
        ClickHouseValues.ApplyPagination(command, page, size);
        ...
    }
}
```

## Проверка схемы

DDL ClickHouse миграции сервиса не запускают. Insert со списком колонок падает на колонке, которой нет в
таблице, и путь записи, который питают события, перестаёт продвигаться без видимых признаков. Сервис
вызывает проверку при старте для каждой таблицы, в которую пишет:

```csharp
await guard.EnsureColumnsAsync("reports.usage", UsageWriter.Columns, "Apply deploy/clickhouse/002-usage-amount.sql.");
```

Сообщение различает отсутствующую таблицу и отсутствующую колонку. При blue-green новая версия тогда не
получает трафик, а работающая продолжает обслуживать.

## Настройки

| Настройка | |
|---|---|
| `ClickHouse:Enabled` | `true`; `false` заставляет подключение отвечать 503, убирает проверку готовности и требование строки подключения |
| `ClickHouse:ConnectionString` | `Host=...;Port=8123;Username=...;Password=...;Database=...`, из окружения или хранилища секретов |
| `ClickHouse:CommandTimeoutSeconds` | 30 |
| `ClickHouse:MaxInsertBlockRows` | 10 000: самый большой insert, который уходит одним блоком, и поэтому самый большой, который покрывает серверная дедупликация |

Под docker compose инфраструктура получает сервер ClickHouse; учётные данные берутся из
`deploy/compose/.env`.

## Тесты

Тест сервиса не ходит в ClickHouse: он даёт сервису in-memory двойник порта читателя или писателя, как
для любого другого порта. SQL тестируется на реальном сервере: `ClickHouseTestDatabase` в
`Common.Tests` создаёт собственную базу теста на сервере из `TEST_CLICKHOUSE` и удаляет её после; тест
создаёт свои таблицы через `ExecuteAsync`. Без переменной тест пропускается.

```bash
eval "$(bash tests/servers/up.sh)"     # тестовые серверы, среди них ClickHouse (tests/servers/clickhouse.sh)
dotnet test --filter "FullyQualifiedName~ClickHouse"
```

Так `Common.Tests` проверяет ядро: подходящая таблица проходит, отсутствующая колонка и отсутствующая
таблица останавливают старт с нужным сообщением, постраничное чтение возвращает типизированные значения,
готовность и выключатель.
