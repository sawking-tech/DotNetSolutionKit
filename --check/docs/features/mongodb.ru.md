# MongoDB

`--MongoDB` добавляет отдельный проект `Capabilities.Mongo` с тем, что нужно сервису, который хранит документы в
MongoDB рядом с основной базой, и регистрирует это в сервисе, который ссылается на проект. MongoDB не заменяет `--Database`: сущности, миграции и outbox остаются
в PostgreSQL или SQL Server. Запросы остаются в сервисе: читатель или писатель - класс инфраструктуры
сервиса за портом его слоя приложения.

| Часть | |
|---|---|
| `IMongoStore` | база сервиса и её коллекции поверх одного клиента на процесс; 503, когда выключено |
| `Guid` | свойство `Guid` хранится как стандартный подтип UUID, без которого драйвер отказывается его писать; `[BsonGuidRepresentation]` на свойстве выбирает другой, а `Guid` внутри `object` или словаря объектов идёт через сериализатор объектов |
| готовность | `/ready` шлёт `ping` в базу сервиса, пока MongoDB включён, и сдаётся через 5 секунд |

```csharp
public sealed class ReceiptStore(IMongoStore mongo) : IReceiptStore
{
    private IMongoCollection<Receipt> Receipts => mongo.Collection<Receipt>("receipts");

    public Task SaveAsync(Receipt receipt, CancellationToken ct) =>
        Receipts.ReplaceOneAsync(r => r.Id == receipt.Id, receipt, new ReplaceOptions { IsUpsert = true }, ct);

    public Task<Receipt?> FindAsync(Guid id, CancellationToken ct) =>
        Receipts.Find(r => r.Id == id).FirstOrDefaultAsync(ct)!;
}
```

Клиент подключается при первом обращении, поэтому сервис с выключенным MongoDB не открывает ни одного
сокета. Индексы сервис создаёт сам, на старте, через `Indexes` коллекции.

## Настройки

| Настройка | |
|---|---|
| `MongoDB:Enabled` | `true`; `false` - база отвечает 503, проверка готовности снимается, строка подключения и база не требуются |
| `MongoDB:ConnectionString` | `mongodb://user:password@host:27017/?authSource=admin`, из окружения или хранилища секретов; проверяется на старте |
| `MongoDB:Database` | собственная база сервиса; при генерации - имя сервиса |

Под docker compose инфраструктура получает сервер MongoDB; учётные данные берутся из
`deploy/compose/.env`.

## Тесты

Тест сервиса до MongoDB не доходит: он даёт сервису двойник в памяти для порта читателя или писателя, как
для любого другого порта. Драйвер проверяется на настоящем сервере: `MongoTestDatabase` в `Capabilities.Mongo.Tests`
называет собственную базу теста на сервере из `TEST_MONGO` и удаляет её после. Без переменной тест
пропускается.

```bash
eval "$(bash tests/servers/up.sh)"     # тестовые серверы, среди них MongoDB (tests/servers/mongo.sh)
dotnet test --filter "FullyQualifiedName~Mongo"
```

`Capabilities.Mongo.Tests` так проверяет ядро: записанный документ читается таким же, вместе с `Guid`, готовность
сообщает о сервере; без сервера - какие настройки обязательны, кривая строка подключения и переключатель.
