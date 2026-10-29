# Журнал аудита

Генерируется с `--Audit` вместе с `--Messaging outbox`, по умолчанию выключено. Без outbox флаг ничего не
делает, и генерация об этом сообщает: запись должна закоммититься вместе с изменением, которое она
описывает, а сообщение в той же транзакции пишет только outbox. Оба флага передаются с `--Solution` и снова
каждому сервису, который генерируется позже.

## Что записывается

Сущность с `[Auditable]` публикует `AuditRecordedV1` на каждую созданную, изменённую или удалённую строку:

```csharp
[Auditable("Orders", DisplayProperty = nameof(Number), SubjectTenantProperty = nameof(TenantId))]
public class Order : AggregateRoot<Guid>
{
    public string Number { get; private set; }
    public Guid TenantId { get; private set; }

    [AuditRedact] public string ReturnSecret { get; private set; }   // the change, not the value
    [AuditIgnore] public int SyncAttempts { get; private set; }      // moves without meaning
}
```

Запись несёт модуль, сущность, её id и отображаемое значение, действие, diff
`{"Status":{"old":"Draft","new":"Placed"}}`, кто действовал и от имени какого тенанта, тенант, которому
принадлежит строка, сервис, трейс и [идентификатор корреляции](../architecture/web-layer.ru.md#корреляция).

- Интерсептор публикует внутри `SaveChanges`, в outbox через тот же контекст: запись коммитится вместе
  с изменением, а откаченное изменение не оставляет записи.
- Создание записывает каждое выставленное значение, включая `false`; изменение - то, что поменялось;
  удаление - только действие. `CreatedAt`, `UpdatedAt`, ключ и токены конкурентности не пишутся.
- Объект-значение, которым владеет сущность, раскладывается в её diff как `DeliveryAddress.City`; при
  замене целиком прежние значения всё равно попадают в запись.
- Джоб или консьюмер без пользователя записывается как система, `Guid.Empty`; если актора не удалось
  прочитать, сохранение не падает.

## Записи, которых не видит change tracker

`ExecuteUpdateAsync`, `ExecuteDeleteAsync` и сырой SQL обходят интерсептор. Если такая запись несёт
решение оператора, её записывают явно:

- `IAuditRecorder.RecordAsync` - для одного изменения, описанного вручную;
- `ISetBasedAuditCapture.BeginAsync(query, module)` перед выражением и `CompleteAsync()` после него, в
  той же транзакции: читает затронутые строки до и после и публикует `AuditBulkRecordedV1`, который
  консьюмер разворачивает в записи по строкам. Больше 5 000 строк - публикует число вместо строк.

## Проверки

Сгенерированный сервис запускает `AuditGuardTests`:

| Проверка | Падает, когда |
|---|---|
| для каждой сущности принято решение | на сущности нет ни `[Auditable]`, ни `[AuditIgnore]` |
| маркеры называют реальные свойства | `DisplayProperty` или `SubjectTenantProperty` называет несуществующее свойство, или subject не `Guid` |
| в журнале нет секретов | у свойства аудируемой сущности с именем как у пароля, токена, ключа, хеша или соли нет ни `[AuditRedact]`, ни `[AuditIgnore]` |
| массовые записи объявлены | метод в `*Repository.cs` вызывает `ExecuteUpdateAsync`, `ExecuteDeleteAsync` или сырой SQL без `[SetBasedWrite(audited, reason)]` |

Последняя проверка читает исходники: вызов внутри тела метода рефлексии не виден.

## Хранение журнала

Шаблон публикует и не хранит. Сервис, который ведёт журнал, принимает оба сообщения в свою таблицу и
решает, кто что может читать, например по `ActorTenantId` и `SubjectTenantId`:

```csharp
public sealed class AuditRecordedConsumer(JournalDbContext db) : IConsumer<AuditRecordedV1>
{
    public async Task Consume(ConsumeContext<AuditRecordedV1> context)
    {
        db.Entries.Add(JournalEntry.From(context.Message));
        await db.SaveChangesAsync(context.CancellationToken);
    }
}
```

## Тесты

`Common.Tests` покрывает интерсептор (создание, изменение, удаление, owned-значения, коллекции, даты,
скрытие значений, тенант субъекта, системный актор, идентификатор корреляции) и массовый захват.
Проверено вживую под compose: созданный заказ опубликовал один `AuditRecordedV1` с идентификатором
корреляции запроса, а откаченный не опубликовал ничего.
