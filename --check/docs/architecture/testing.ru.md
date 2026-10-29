# Тестирование

Какой вид теста получает код и почему большинство тестов работает на in-memory базе:
[ADR-005](../adr/005-testing-a-service.md). Эта страница - о том, как начать.

Тестовый проект сервиса ссылается на `Common.Testing`: тестовые контексты, тестовые базы СУБД решения,
заглушки для времени и пользователя, проверки правил. Тестового фреймворка в нём нет; его выбирает
тестовый проект сервиса и задаёт в `TestSkip.Handler`, как этот фреймворк пропускает тест.
`Common.Tests` - тестовый проект самого `Common`.

`--TestFramework xunit` генерирует тесты сервиса на xUnit v3 вместо NUnit; инфраструктура та же, и
тесты те же. Фреймворк знает один файл тестового проекта, `TestFramework.cs`: в нём атрибуты, которые
носят тесты, - `[Test]`, `[TestOf]`, `[Integration]`, `[RunsInParallel]`, `[RunsAlone]` - поверх атрибутов
самого фреймворка, поэтому в тесте нет `#if`, а смена фреймворка трогает только этот файл.
`[Integration]` - категория в NUnit и trait `TestCategory` в xUnit, поэтому фильтры
`TestCategory=Integration` и `TestCategory!=Integration` отбирают одни и те же тесты в обоих.

## Тест сервиса

Базовый класс строит контекст для одного тестируемого класса: сам класс, его настоящие репозитории и
`DbContext` на in-memory провайдере, а моки - только для того, что выходит за пределы сервиса.

```csharp
public abstract class OrderServiceTestBase
{
    protected InMemoryTestExecutionContext<OrderService, OrdersDbContext> CreateTestContext()
    {
        var context = new InMemoryTestExecutionContext<OrderService, OrdersDbContext>();

        var mail = new Mock<IMailSender>();
        context.Register(mail);                    // the mock, to verify after the act
        context.Register(mail.Object);             // the service the class under test receives

        context.Register<IOrderRepository, OrderRepository>();
        context.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrdersDbContext>());
        return context;
    }
}
```

Одна fixture на метод, в `OrderService.Place.Tests.cs`:

```csharp
[TestOf(typeof(OrderService))]
[RunsInParallel]
public class OrderServicePlaceTests : OrderServiceTestBase
{
    [Test(Description = "A placed order is saved with its lines")]
    public async Task Should_SaveTheOrder_When_TheCartHasLines()
    {
        await using var ctx = CreateTestContext();
        await ctx.ArrangeAsync(Customer);

        var orderId = await ctx.ActAsync(s => s.PlaceAsync(new PlaceOrder(Customer.Id, Lines), default));

        await ctx.AssertAsync(async db => (await db.Orders.FindAsync(orderId)).ShouldNotBeNull());
    }
}
```

У каждого теста своя база, поэтому fixture'ы идут параллельно. `ActAsync` и `AssertAsync` работают в
разных scope, поэтому проверка читает то, что сохранено.

## Выбор контекста

| Контекст | Для чего |
|---|---|
| `TestExecutionContext` | только контейнер |
| `ServiceTestExecutionContext<TService>` | класс без базы |
| `DbTestExecutionContext<TDbContext>` | база без тестируемого класса |
| `ServiceDbTestExecutionContext<TService, TDbContext>` | класс и база на провайдере, который настраиваете вы |
| `InMemoryTestExecutionContext<TService, TDbContext>` | класс и in-memory база с подключёнными доменными событиями |

## NUnit, xUnit и Shouldly

Тесты сервиса по умолчанию на NUnit, с `--TestFramework xunit` - на xUnit v3; `Common.Tests` остаётся на
NUnit. `Common.Testing`, на который ссылаются оба, от тестового фреймворка не зависит: каждый in-memory
контекст называет свою базу новым Guid, а каждый тестовый проект через `TestSkip.Handler` говорит ему,
как его фреймворк пропускает тест. Параллельность у каждого фреймворка своя: NUnit запускает fixture с
`[RunsInParallel]` рядом с другими, xUnit запускает классы тестов параллельно, а тесты одного класса -
друг за другом. NUnit, как и xUnit, создаёт fixture на каждый тест, поэтому в обоих тест готовится в
конструкторе и убирает за собой в `Dispose`.

Для проверок - Shouldly под лицензией MIT; FluentAssertions с версии 8 перешёл на коммерческую лицензию,
которая не подходит шаблону под MIT.

## Интеграционные тесты

То, что зависит от PostgreSQL (транзакции и outbox, ограничения, `ILIKE` и сырой SQL, миграции),
проверяется на настоящей базе в тестах категории `TestCategory=Integration`: `[Integration]` в
сервисе, `[Category(TestCategories.Integration)]` в `Common`. Они
читают строку подключения из `TEST_POSTGRES` и пропускаются с этой причиной, если переменная не задана,
поэтому обычному `dotnet test` база не нужна:

```bash
eval "$(bash tests/servers/up.sh)"     # все тестовые серверы решения и их переменные TEST_*
dotnet test --filter "TestCategory=Integration"
bash tests/servers/up.sh down          # убирает контейнеры

dotnet test --filter "TestCategory!=Integration"   # everything else
```

Каждый сервер - скрипт в `tests/servers/`: `postgres.sh` и по одному на флаг, которому нужен сервер, с
командами `start`, `ready`, `logs` и `down`. `up.sh` запускает все скрипты, которые там находит, и CI
запускает те же скрипты, поэтому флаг, добавленный позже, приносит свой скрипт и ничего общего не меняет.

Решение, сгенерированное с `--Database mssql`, читает вместо неё `TEST_SQLSERVER`, а тесты сервиса берут
`SqlServerDbTestExecutionContext` вместо `PostgresDbTestExecutionContext`. Схема сервиса один раз за прогон
мигрируется в шаблонную базу, с неё снимается backup, и каждый тест восстанавливает его под своим именем:
на SQL Server это быстрее, чем мигрировать базу каждого теста. SQL Server для них поднимает `up.sh`.

Такие fixture'ы есть в `Common.Tests/Integration`: для номеров из последовательности и для объектного
хранилища.

Тест, который регистрирует доменные события (`AddDomainEvents(...)` в `configure`), получает их и на
настоящей базе: интеграционные контексты подключают интерсепторы доменных событий, а `ActAsync` публикует
им свой scope, как in-memory контекст. `PostgresDomainEventPhasesTests` и `SqlServerDomainEventPhasesTests`
проверяют там фазы после сохранения одной командой, коммита и отката.

## Покрытие

Тестовые проекты ссылаются на `coverlet.collector`, поэтому покрытие не требует настройки:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Каждый тестовый проект пишет отчёт Cobertura в `TestResults/`. С `--GitHubCiCd` покрытие снимается по
запросу, а не на каждом пулл-реквесте: `coverage.yml` даёт отчёт по каждому сервису и по `Common` для
unit- и интеграционных прогонов и может падать по порогу покрытия ветвей; см. [CI](../features/ci.md).
