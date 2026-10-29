# Хранение

EF Core на PostgreSQL или, с `--Database mssql`, на SQL Server. У каждого сервиса свой `DbContext` и своя
схема базы, названная по сервису (`orders` или `sales_orders` для `Sales.Orders`); рядом Hangfire получает
вторую схему (`orders_hangfire`).

Разделы ниже описывают PostgreSQL; чем отличается SQL Server, сказано в разделе [SQL Server](#sql-server).
Код, который сервис пишет поверх `Common`, для обеих СУБД один.

## Сервис владеет своей схемой

Сервисы могут делить одну базу PostgreSQL, и сама база не мешает двум сервисам писать в одну схему.
Мешает `PostgresSchemaGuard` при старте:

1. создаёт схему, если её нет;
2. под блокировкой строки читает владельца, записанного в таблице `_service_metadata` схемы;
3. записывает сервис владельцем, если владельца нет, и не даёт сервису стартовать, если владелец - другой
   сервис.

Владелец - namespace домена сервиса. Скопированная строка подключения или переименованный сервис падает
на старте с именем владельца и не пишет в таблицы чужого сервиса.

Если база ещё стартует, например когда сервис и база запущены одновременно, guard ждёт: до 15 попыток с
интервалом 2 секунды, пока PostgreSQL отвечает "starting up" (SQLSTATE 57P03) или его порт отклоняет
подключения.

Почему свой guard: в PostgreSQL нет понятия схемы, принадлежащей приложению, а альтернативы, отдельная
база или отдельный пользователь базы на сервис, - это инфраструктура, на которую шаблон не может
рассчитывать.

## Миграции идут под блокировкой

`MigrationRunner.RunMigrations` применяет миграции при старте, держа advisory lock PostgreSQL, ключ
которого выводится из имени схемы. Две реплики одного сервиса, стартовавшие вместе, не мигрируют схему
дважды: вторая ждёт первую и не находит работы.

Сервис без единой миграции пишет в лог fatal-строку с командой, которая добавляет первую; см.
[генерацию решения](../getting-started/generating-a-solution.ru.md#первая-миграция).

## Репозитории

Репозиторий принимает запрос в виде спецификации, а фильтрация, пагинация, сортировка и include написаны
один раз, в `EntityFrameworkRepository`:

```csharp
public interface IOrderRepository : ISpecificationRepository<Order, Guid>;

public sealed class OrderRepository(OrdersDbContext context)
    : EntityFrameworkRepository<Order, Guid, OrdersDbContext>(context), IOrderRepository
{
    protected override IReadOnlyDictionary<string, string> SortFields { get; } =
        new Dictionary<string, string> { ["placedAt"] = nameof(Order.PlacedAt) };
}

var page = await orders.ListPageAsync(
    new QuerySpecification<Order>(new OrdersOfCustomer(customerId)).Include(o => o.Lines),
    request);
```

Почему так: [ADR-002](../adr/002-repositories-on-specifications.md).

## Поиск без учёта регистра

`ICaseInsensitiveSearch` строит условие поиска как обычную спецификацию, поэтому оно комбинируется с
другими через `&` и `|`:

```csharp
var query = new QuerySpecification<Order>(
    new OrdersOfCustomer(customerId) & search.GetSpecification<Order>(o => o.Customer.Email, term));
```

Реализация для PostgreSQL переводит его в `ILIKE` и экранирует `%`, `_` и сам символ экранирования, так
что ввод пользователя сопоставляется буквально. Тесты на in-memory базе регистрируют вместо неё in-memory
реализацию; см. [ADR-005](../adr/005-testing-a-service.md).

## Читаемые номера

Номер заказа или счёта, который люди читают и набирают, берётся из последовательности PostgreSQL до
создания сущности, поэтому события, которые она поднимает в конструкторе, уже несут номер:

```csharp
var number = await shortIds.GetNextAsync("orders.order_number_seq", ct);
var order = new Order(context, number, ...);
```

`IShortIdGenerator` зарегистрирован в сгенерированном сервисе, последовательность создаёт миграция.
Каждое значение выдаётся один раз, даже если его транзакция откатилась, так что пропуски в нумерации -
норма. Имя последовательности передаётся в запрос параметром и никогда не попадает в его текст.

## Unit of work

`IUnitOfWork` - граница транзакции use case; `DbContext` сервиса реализует его через `DbContextBase`.
Нарушение уникального ограничения при сохранении превращается в `UniqueViolationException`, а запись,
проигравшая гонку по токену конкурентности (`xmin` в PostgreSQL, `rowversion` в SQL Server), - в
`ConcurrencyException`; на оба API отвечает 409.

## Команды, которые клиент может повторить

Клиент повторяет запрос, когда рвётся соединение или очередь доставляет сообщение заново, и не знает,
прошла ли первая попытка. Команда, которая не должна создать вторую копию, несёт ключ, выбранный
клиентом, а `IIdempotentExecutor` выполняет её работу один раз на ключ:

```csharp
public sealed record CreateOrder(..., string IdempotencyKey) : IIdempotentRequest;

return await idempotent.ExecuteAsync(request, "orders.create", _ =>
{
    var order = new Order(context, ...);
    orders.Add(order);
    return Task.FromResult(order.ToResponse());
}, ct);
```

- Первый запрос выполняет работу и в той же транзакции записывает ключ и ответ; повтор получает этот
  ответ и ничего не делает.
- Два запроса с одним ключом в один момент оба выполняют работу, и уникальный индекс журнала даёт
  закоммитить одному. Второй откатывается, сбрасывает отслеживаемые изменения и отвечает результатом
  победителя.
- Ключ, использованный для другой операции, отклоняется с 409 `IDEMPOTENCY_KEY_REUSED`. Ключ короче 16
  или длиннее 128 символов отклоняется до начала работы.
- Неудачная попытка ничего не записывает, поэтому повтор выполняет работу. Дубликат, который отклоняет
  сама работа, например занятое имя, доходит до вызывающего как этот конфликт: повтор упал бы так же.
- `ExecuteOnceAsync` - для ответа, который нельзя хранить, например секрета, показанного один раз; повтор
  отклоняется с 409 `IDEMPOTENCY_ALREADY_CARRIED_OUT`.
- Ключи ограничены тенантом, от имени которого действует вызывающий, а без тенанта - аккаунтом.

Журнал - таблица в схеме сервиса. По умолчанию её нет; сервис, которому она нужна, добавляет её в
модель и регистрирует executor:

```csharp
// OnModelCreating, then add a migration
modelBuilder.AddIdempotencyLog();

// Infrastructure DependencyInjection
services.AddIdempotency<OrdersDbContext>();
```

## SQL Server

`--Database mssql` генерирует решение на SQL Server. У каждой части выше есть реализация для SQL Server за
тем же швом, и в сгенерированное решение попадает только одна из двух:

| Часть | PostgreSQL | SQL Server |
|---|---|---|
| провайдер EF | Npgsql | `Microsoft.EntityFrameworkCore.SqlServer` |
| guard схемы | `PostgresSchemaGuard`, блокировка строки | `SqlServerSchemaGuard`, `sp_getapplock` на время транзакции |
| блокировка миграций | advisory lock | `sp_getapplock` на время сессии |
| нарушение уникальности | SQLSTATE 23505 | ошибки 2627 и 2601 |
| поиск без учёта регистра | `ILIKE` | `LIKE` в collation `Latin1_General_100_CI_AS` |
| читаемые номера | `nextval` | `sp_sequence_get_range` |
| хранилище Hangfire | `Hangfire.PostgreSql` | `Hangfire.SqlServer` |
| outbox | MassTransit на PostgreSQL | MassTransit на SQL Server |

Какая из двух баз в решении, знает одно место - `DatabaseProvider` в `Common.Infrastructure`: сервис и
`Common` берут у него провайдер EF, guard схемы, блокировку миграций, блокировки и статистику outbox, поиск
и читаемые номера и сами провайдер не называют. У Hangfire своё хранилище на каждую базу, поэтому
`DependencyInjection.BackgroundJobs.cs` сервиса - второе место, которое её знает. Третья база трогает эти
два файла и реализации за ними.

Что заметит команда:

- Guard создаёт базу из строки подключения, если её нет: контейнер SQL Server стартует с одной `master`.
  Существующая база берётся как есть: право создавать базы нужно, только пока базы ещё нет.
- Guard ждёт до 30 попыток с интервалом 2 секунды, пока сервер стартует (ошибки 4060, 53, 40 и таймауты).
- Поиск сравнивает в регистронезависимом collation, каким бы ни был collation колонки, и экранирует `%`,
  `_` и `[` через `/`. Тесты на in-memory базе регистрируют `InMemorySqlServerCaseInsensitiveSearch`,
  который сравнивает так же.
- Таблицы Hangfire ставятся при старте под одной блокировкой на всю базу: два сервиса, которые ставят их в
  одну базу одновременно, ловят в SQL Server deadlock, и Hangfire сдаётся после трёх попыток.
- Последовательность для читаемых номеров создаётся `CREATE SEQUENCE` в миграции; генератор получает её
  имя параметром, как на PostgreSQL.
- Миграции сервиса, сгенерированного под PostgreSQL, к SQL Server не применяются: сервис генерируется под
  одну СУБД с самого начала, и миграции добавляются уже под неё.

## Тесты

`Common.Tests` покрывает базовый репозиторий, спецификации, сортировку, `DbContextBase` и ключ блокировки
migration runner. Guard, миграциям и трансляции в `ILIKE` нужен PostgreSQL, они относятся к
интеграционным тестам. Idempotent executor тестируется на СУБД решения: повтор, гонка на одном ключе,
повторно использованный и невалидный ключ, неудачная попытка, собственный дубликат работы, два тенанта с
одним ключом. На SQL Server тем же интеграционным тестам нужен `TEST_SQLSERVER`; см. [тестирование](testing.ru.md).
