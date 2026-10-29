# Ошибки

Любая ошибка, которую возвращает сервис, - это problem по RFC 9457, `application/problem+json`, откуда бы
она ни пришла: брошенное исключение, проваленная валидация, пустой 404 или 405 от маршрутизации. Успешный
ответ возвращает DTO как есть, без обёртки.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "Order 42 not found",
  "code": "NOT_FOUND",
  "traceId": "00-4a17520d8ee59a250b7c16c87bdbce4f-cea6178babc50d48-00",
  "correlationId": "4a17520d8ee59a250b7c16c87bdbce4f"
}
```

| Поле | Значение |
|---|---|
| `status`, `title`, `detail` | RFC 9457: HTTP-статус, его название и описание ошибки словами |
| `code` | то, по чему сверяется код клиента; не меняется при смене формулировок |
| `traceId` | W3C-идентификатор трассы запроса |
| `correlationId` | идентификатор из `X-Correlation-Id`, тот же, что в заголовке ответа и в логах |
| `errors` | только для валидации: отклонённые поля, названные как в JSON (`name`, а не `Name`) |

## Исключения

Код бросает исключения из `Common/Exceptions`, а `PlatformExceptionMapper` превращает их в problem:

| Исключение | Статус | `code` |
|---|---|---|
| `BadRequestException`, `JsonException`, `InconsistentDataException` | 400 | свой или `BAD_REQUEST` |
| `UnauthorizedAccessException`, `SecurityTokenException` | 401 | `UNAUTHORIZED`, `INVALID_TOKEN` |
| `AccessDeniedException` | 403 | свой или `FORBIDDEN` |
| `NotFoundException` | 404 | `NOT_FOUND` |
| `ConflictException`, `ConcurrencyException`, `UniqueViolationException`, `RequestDuplicationException` | 409 | свой или `CONFLICT` |
| `BusinessLogicException`, `ValidationException` (FluentValidation или data annotations) | 422 | свой или `VALIDATION_ERROR` |
| `RateLimitException` | 429, с `Retry-After`, если в исключении есть задержка | `RATE_LIMIT` |
| `OperationCanceledException` | 499 | `REQUEST_CANCELLED` |
| `ServiceUnavailableException` | 503 | свой или `SERVICE_UNAVAILABLE` |
| всё остальное, `ConfigurationException` | 500 | `INTERNAL_ERROR` |

Сообщение непредвиденного исключения доходит до клиента только вне production, а сообщение
`ConfigurationException` - никогда: в обоих часто есть имена хостов, таблиц и секреты.

Отменённый запрос получает 499, поэтому ушедший клиент не попадает в долю 5xx, за которой следит
алертинг. Отклонённый запрос пишется в лог как warning, а не как error.

## Собственные правила сервиса

Сервис, который бросает своё исключение или хочет другой ответ на общее, регистрирует `IExceptionMapping`.
Зарегистрированные правила проверяются раньше общих:

```csharp
public sealed class OrdersExceptionMapping : IExceptionMapping
{
    public ProblemDetails? TryMap(Exception exception) => exception switch
    {
        OrderLockedException ex => PlatformExceptionMapper.Problem(423, ex.Message, "ORDER_LOCKED"),
        _ => null,
    };
}

services.AddSingleton<IExceptionMapping, OrdersExceptionMapping>();
```

## Почему problem и без обёртки

RFC 9457 - стандартный формат, и ASP.NET Core уже пишет его для валидации и status code pages, так что
один writer обслуживает все источники ошибок. Обёртка вокруг успешных ответов добавляет уровень, который
каждый клиент должен разворачивать, и не несёт ничего сверх статуса и тела.

## Тесты

`Common.Tests` фиксирует маппинг каждого исключения и проверяет на тестовом сервере, что получает клиент:
исключение, неизвестный маршрут, ошибку валидации, rate limit с `Retry-After` и возврат идентификатора
корреляции, который прислал вызывающий.
