---
name: add-controller
description: Scaffold an API controller of this solution - routes and contracts in Common.Contracts, permissions on every action, status codes by meaning, idempotent creates.
---

Scaffold a controller with its route constants and contracts.

## Usage
`/add-controller <ResourceName>` - e.g. `/add-controller Invoices`

## What to do

Ask the user (in their language):
1. Which service?
2. Which operations?
3. The permission group name (e.g. `InvoicePermissions`)?
4. Is the service behind the API gateway? A path under `/api/{version}/<resource>` is reached through the
   gateway once the gateway has a route for it: [API gateway](https://dotskit.sawking.tech/docs.html#gateway).

Then scaffold the files below. The service's own guide is `Controllers/README.md` in its API project; the
web pipeline: [web layer](https://dotskit.sawking.tech/docs.html#web-layer).

---

## Idempotency for creating endpoints - mandatory

A client retries when a connection drops, and it cannot tell whether the first attempt went through. An
endpoint that creates something carries a key the client chose:

```csharp
public sealed record CreateInvoiceRequest : IIdempotentRequest
{
    /// <summary>
    /// Chosen by the client and stable across retries of the same request, 16..128 characters. A repeat
    /// gets the first answer.
    /// </summary>
    public required string IdempotencyKey { get; init; }
    // ... other fields
}
```

The service runs the work through `IIdempotentExecutor` (`/add-service-class`): the first request does it
and records the answer, a repeat gets that answer and creates nothing, a key reused for another operation
answers 409. Never answer a repeat with an empty 200.

Not idempotent and not keyed: notifications, side-effect-only confirmations, reads.

## Validation

A validator is a FluentValidation class in the API assembly, found by scanning; an invalid request is
refused before the action with 422 and the fields named:
[validation and pagination](https://dotskit.sawking.tech/docs.html#validation).

```csharp
public sealed class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty().Length(16, 128);
        RuleFor(x => x.CustomerName).MustBeSafeNormalizedText().MustBeWithinNormalizedLength(200);
    }
}
```

Text one person types and another reads goes through `MustBeSafeNormalizedText`; an e-mail through
`MustBeNormalizedEmailAddress`.

## File 1 - Controller

Location: `NamespaceRoot.ProductName.<Service>.API/Controllers/<ResourceName>Controller.cs`

```csharp
/// <summary>
/// Manages invoices.
/// </summary>
[ApiController]
[Authorize]
[Route(InvoiceRoutes.Root)]
public class InvoicesController(IInvoiceService service) : ControllerBase
{
    /// <summary>Returns the invoices matching the filter.</summary>
    /// <param name="filter">Filter, sorting and page.</param>
    [HttpGet(InvoiceRoutes.GetAll)]
    [RequiredPermissions(InvoicePermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<GetInvoicesResponse> GetAll([FromQuery] InvoiceFilter filter)
        => service.GetAllAsync(filter, HttpContext.RequestAborted);

    /// <summary>Returns one invoice.</summary>
    /// <param name="id">Invoice identifier.</param>
    [HttpGet(InvoiceRoutes.GetById)]
    [RequiredPermissions(InvoicePermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<InvoiceResponse> GetById([FromRoute] Guid id)
        => service.GetByIdAsync(id, HttpContext.RequestAborted);

    /// <summary>Creates an invoice.</summary>
    /// <param name="request">Invoice data.</param>
    [HttpPost(InvoiceRoutes.Create)]
    [RequiredPermissions(InvoicePermissions.Create)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<InvoiceResponse>> Create([FromBody] CreateInvoiceRequest request)
    {
        var created = await service.CreateAsync(request, HttpContext.RequestAborted);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
```

**Rules:**
- Reads return the service's `Task<T>` directly: no `async`/`await`, no `IActionResult`, so Swagger
  documents the type.
- A create returns `ActionResult<T>` through `CreatedAtAction`: 201 with `Location`. It is the one place a
  controller awaits.
- No `try`/`catch`: throw the exceptions from `Common/Exceptions`, the shared handler maps them
  ([errors](https://dotskit.sawking.tech/docs.html#errors)).
- `[RequiredPermissions(...)]` on every action; an action without it is not checked.
- `HttpContext.RequestAborted` is the cancellation token, always passed on.
- An XML `<summary>` on the class and every action and parameter: they become the Swagger descriptions.
- `[ProducesResponseType]` for every status the action can answer.
- No branching in a controller: it delegates.

### Status codes

| Operation | Success | Errors |
|---|---|---|
| read one by id | 200 | 404 not found, 403 |
| list | 200 | 422 bad page or filter, 400 sort field not allowed |
| create | 201 with `Location` | 409 duplicate or key reused, 422 invalid |
| change | 200 with the new state, or 204 | 404, 409 conflict, 422 |
| delete | 204 | 404 |
| a business rule refuses | - | 422 (`BusinessLogicException`) |

---

## File 2 - Route constants

Location: `src/common/NamespaceRoot.ProductName.Common.Contracts/<Service>Service/<ResourceName>/<ResourceName>Routes.cs`

**Always in `Common.Contracts`:** a client of the service (another service, the gateway's Swagger, a
typed or Refit client) and the service read the same strings.

```csharp
/// <summary>Invoices API endpoints.</summary>
public static class InvoiceRoutes
{
    private const string ApiWithVersion = "/api/v1/";

    /// <summary>Invoices root.</summary>
    public const string Root = ApiWithVersion + "invoices";

    /// <summary>All invoices.</summary>
    public const string GetAll = "";

    /// <summary>One invoice.</summary>
    public const string GetById = "{id}";

    /// <summary>Create an invoice.</summary>
    public const string Create = "";

    /// <summary>Change an invoice.</summary>
    public const string Update = "{id}";

    /// <summary>Delete an invoice.</summary>
    public const string Delete = "{id}";
}
```

Swagger builds one document per version from the version in the path.

**A versioned route is public.** A path under `/api/v1/` is meant for clients; naming it `internal`
(`/api/v1/internal/invoices`) does not make it internal, and through the gateway anyone with a token
reaches it. An endpoint meant only for other services stays out of the gateway's routes and is called with
the internal key ([calling another service](https://dotskit.sawking.tech/docs.html#auth)).

---

## File 3 - Request and response contracts

Location: next to the routes, `src/common/NamespaceRoot.ProductName.Common.Contracts/<Service>Service/<ResourceName>/`.

```csharp
// A paged list
public sealed record GetInvoicesResponse : BaseResponse, IPaginatedResponse<InvoiceItem>
{
    [JsonPropertyName("invoices")]
    public required IReadOnlyList<InvoiceItem> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

// A list without pages
public sealed record GetCurrenciesResponse : BaseResponse, IItemsResponse<CurrencyItem>
{
    [JsonPropertyName("currencies")]
    public required IReadOnlyList<CurrencyItem> Items { get; init; }
}

// The filter
public sealed record InvoiceFilter : IPaginationRequest, ISortableRequest, ISearchableRequest
{
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public SortDirection SortDir { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

- A list response implements `IItemsResponse<T>` or `IPaginatedResponse<T>`, never a bare array.
- `Items` carries a `[JsonPropertyName]` with the semantic name (`invoices`), so the JSON says what it
  holds.
- The sort keys a filter accepts are the JSON fields of the item (`/add-repository`).
- No response carries a token: `ResponseContractTests` fails on a property named `Token`, `AccessToken`,
  `RefreshToken` and the like.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
