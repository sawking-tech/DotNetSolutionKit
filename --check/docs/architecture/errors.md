# Errors

Every error a service returns is an RFC 9457 problem, `application/problem+json`, whatever produced it: a
thrown exception, failed validation, or an empty 404 or 405 from routing. Successful responses return the
DTO as is, with no envelope around it.

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

| Field | Meaning |
|---|---|
| `status`, `title`, `detail` | RFC 9457: the HTTP status, its name, and what went wrong in words |
| `code` | what client code matches on; stable across wording changes |
| `traceId` | the W3C trace identifier of the request |
| `correlationId` | the identifier from `X-Correlation-Id`, the same as on the response header and in the logs |
| `errors` | for validation only: the rejected fields, named as in the JSON (`name`, not `Name`) |

## Exceptions

Code throws the exceptions from `Common/Exceptions`; `PlatformExceptionMapper` turns them into problems:

| Exception | Status | `code` |
|---|---|---|
| `BadRequestException`, `JsonException`, `InconsistentDataException` | 400 | its own, or `BAD_REQUEST` |
| `UnauthorizedAccessException`, `SecurityTokenException` | 401 | `UNAUTHORIZED`, `INVALID_TOKEN` |
| `AccessDeniedException` | 403 | its own, or `FORBIDDEN` |
| `NotFoundException` | 404 | `NOT_FOUND` |
| `ConflictException`, `ConcurrencyException`, `UniqueViolationException`, `RequestDuplicationException` | 409 | its own, or `CONFLICT` |
| `BusinessLogicException`, `ValidationException` (FluentValidation or data annotations) | 422 | its own, or `VALIDATION_ERROR` |
| `RateLimitException` | 429, with `Retry-After` when it carries a delay | `RATE_LIMIT` |
| `OperationCanceledException` | 499 | `REQUEST_CANCELLED` |
| `ServiceUnavailableException` | 503 | its own, or `SERVICE_UNAVAILABLE` |
| anything else, `ConfigurationException` | 500 | `INTERNAL_ERROR` |

The message of an unexpected exception reaches the client only outside production, and the message of a
`ConfigurationException` never does: both tend to name hosts, tables and secrets.

A cancelled request answers 499, so a client that went away does not count towards the 5xx rate that
alerting watches. A rejected request is logged as a warning, not as an error.

## A service's own rules

A service that throws an exception of its own, or wants a different answer for a shared one, registers an
`IExceptionMapping`; registered rules are consulted before the shared ones:

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

## Why problems and no envelope

RFC 9457 is the standard shape, and ASP.NET Core already writes it for validation and status code pages,
so one writer serves every source of errors. An envelope around successful responses adds a level every
client has to unwrap and carries nothing a status code and the body do not.

## Tests

`Common.Tests` pins the mapping of every exception, and checks on a test server what a client receives: an
exception, an unknown route, a validation failure, a rate limit with `Retry-After`, and the correlation
identifier a caller sends coming back.
